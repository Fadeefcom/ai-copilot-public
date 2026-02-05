import sys
import keyboard
import io
import base64
import requests
from PIL import ImageGrab
from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
                             QPushButton, QComboBox, QCheckBox, QListWidget, 
                             QListWidgetItem, QAbstractItemView, QLabel, QApplication,
                             QFrame, QSizePolicy)
from PyQt6.QtCore import Qt, pyqtSignal, QTimer, QTime
from PyQt6.QtGui import QFont, QColor

from constants import BACKEND_URL, UI_TEXTS, MODELS, SetWindowDisplayAffinity, WDA_EXCLUDEFROMCAPTURE
from widgets import ChatMessage, ChatInput
from threads import SignalRWorker, AudioCaptureThread, TypingIndicator
from constants import COLORS

class ChatWindow(QMainWindow):
    toggle_signal = pyqtSignal()
    action_signal = pyqtSignal(str)
    
    def __init__(self, current_lang='en'):
        super().__init__()
        self.current_lang = current_lang
        self.texts = UI_TEXTS[self.current_lang]
        self.started = False
        
        self.signalr_worker = None
        self.typing_item = None
        self.current_stream_msg_widget = None
        self.current_stream_text = ""

        self.setWindowTitle(self.texts['title'])
        self.resize(1000, 750)
        
        self.setWindowOpacity(0.8)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, False)
        
        self.setWindowFlags(
            Qt.WindowType.Window | 
            Qt.WindowType.WindowStaysOnTopHint | 
            Qt.WindowType.Tool |
            Qt.WindowType.CustomizeWindowHint |
            Qt.WindowType.WindowCloseButtonHint
        )

        self.setStyleSheet(f"""
            QMainWindow {{ background-color: {COLORS['bg']}; }}
            QWidget {{ color: white; font-family: 'Segoe UI', 'Arial'; }}
            QFrame#Header {{ 
                background-color: {COLORS['card']}; 
                border-bottom: 1px solid {COLORS['border']}; 
            }}
            QFrame#Sidebar {{ 
                background-color: {COLORS['card']}; 
                border-left: 1px solid {COLORS['border']}; 
            }}
            QComboBox {{ 
                background-color: {COLORS['secondary']}; 
                border: 1px solid {COLORS['border']}; 
                border-radius: 4px; padding: 2px 10px; 
            }}
            QCheckBox {{ color: {COLORS['text_muted']}; }}
        """)

        self._set_window_affinity()
        self._init_ui()
        
        self.toggle_signal.connect(self.toggle_visibility)
        self.action_signal.connect(self.send_button_prompt)
        
        keyboard.add_hotkey('ctrl+/', lambda: self.toggle_signal.emit())
        keyboard.add_hotkey('f1', lambda: self.action_signal.emit('say'))
        keyboard.add_hotkey('f2', lambda: self.action_signal.emit('followup'))

    def _init_ui(self):
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        self.main_layout = QVBoxLayout(central_widget)
        self.main_layout.setContentsMargins(0, 0, 0, 0)
        self.main_layout.setSpacing(0)

        self.header = QFrame()
        self.header.setObjectName("Header")
        self.header.setFixedHeight(56)
        header_layout = QHBoxLayout(self.header)
        header_layout.setContentsMargins(15, 0, 15, 0)
        
        self.start_button = QPushButton(self.texts['start'])
        self.start_button.setFixedSize(80, 30)
        self.start_button.clicked.connect(self.on_start)
        self.update_btn_style(self.start_button, COLORS['primary'], is_glow=True)

        self.stop_button = QPushButton(self.texts['stop'])
        self.stop_button.setFixedSize(80, 30)
        self.stop_button.clicked.connect(self.on_stop)
        self.stop_button.setVisible(False)
        self.update_btn_style(self.stop_button, COLORS['destructive'])

        header_layout.addWidget(self.start_button)
        header_layout.addWidget(self.stop_button)
        header_layout.addStretch()

        self.timer_label = QLabel("00:00:00")
        self.timer_label.setStyleSheet(f"font-family: 'JetBrains Mono'; color: {COLORS['primary']}; font-size: 14px;")
        self.timer_label.setVisible(False)
        
        self.model_dropdown = QComboBox()
        self.model_dropdown.setFixedWidth(180)

        try:      
            response = requests.get(f"{BACKEND_URL}/llm/models", timeout=2)
            if response.status_code == 200:
                for m in response.json():
                    self.model_dropdown.addItem(m['id'])
            else:
                self.model_dropdown.addItems(MODELS)
        except Exception as e:
            print(f"API Error: {e}")
            self.model_dropdown.addItems(MODELS)

        self.lang_dropdown = QComboBox()
        self.lang_dropdown.addItems(['ru','en'])
        self.lang_dropdown.setFixedWidth(70)
        self.lang_dropdown.setCurrentText(self.current_lang)
        self.lang_dropdown.currentTextChanged.connect(self._change_language)

        self.sidebar_toggle = QPushButton("✕")
        self.sidebar_toggle.setFixedSize(30, 30)
        self.sidebar_toggle.setCheckable(True)
        self.sidebar_toggle.setChecked(True)
        self.sidebar_toggle.setStyleSheet(f"color: {COLORS['primary']}; background: transparent; font-size: 18px; border: none;")
        self.sidebar_toggle.clicked.connect(self.toggle_sidebar)

        header_layout.addWidget(self.timer_label)
        header_layout.addWidget(self.model_dropdown)
        header_layout.addWidget(self.lang_dropdown)
        header_layout.addWidget(self.sidebar_toggle)

        self.main_layout.addWidget(self.header)

        content_area = QHBoxLayout()
        content_area.setSpacing(0)

        chat_container = QVBoxLayout()
        chat_container.setContentsMargins(10, 10, 10, 10) 
        chat_container.setSpacing(5)

        lat_val = "100ms"
        try:
            lat_val = requests.get(f"{BACKEND_URL}/latency", timeout=1).json()
        except: pass

        self.latency_label = QLabel(f"Latency: {lat_val}")
        self.latency_label.setStyleSheet(f"color: {COLORS['primary']}; font-size: 10px; margin-left: 5px;")
        chat_container.addWidget(self.latency_label)

        self.chat_list = QListWidget()
        self.chat_list.setStyleSheet("""
            QListWidget {
                background: transparent;
                border: none;
                outline: none;
            }
        """)
        self.chat_list.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerPixel)
        self.chat_list.setSpacing(8)
        
        self.action_toolbar = QHBoxLayout()
        self.action_toolbar.setSpacing(10)
        self.action_toolbar.setContentsMargins(0, 5, 0, 5) 
        
        self.say_button = self.create_action_btn('say', COLORS['primary'], "F1")
        self.followup_button = self.create_action_btn('followup', COLORS['accent'], "F2")
        self.assist_button = self.create_action_btn('assist', COLORS['warning'], "")

        self.action_toolbar.addWidget(self.say_button)
        self.action_toolbar.addWidget(self.followup_button)
        self.action_toolbar.addWidget(self.assist_button)
        self.action_toolbar.addStretch()

        self.input_box = ChatInput()
        self.input_box.setFixedHeight(80) 
        self.input_box.setStyleSheet(f"""
            ChatInput {{
                background-color: {COLORS['secondary']};
                color: #e2e8f0;
                border: 1px solid {COLORS['border']};
                border-radius: 8px;
                padding: 10px;
                font-size: 13px;
            }}
            ChatInput:focus {{
                border: 1px solid {COLORS['primary']};
            }}
        """)
        self.input_box.send_signal.connect(self.send_message)
        self.input_box.setEnabled(False)

        chat_container.addWidget(self.chat_list, 1)
        chat_container.addLayout(self.action_toolbar)
        chat_container.addWidget(self.input_box)

        self.sidebar = QFrame()
        self.sidebar.setObjectName("Sidebar")
        self.sidebar.setFixedWidth(256)
        sidebar_layout = QVBoxLayout(self.sidebar)
        sidebar_layout.setContentsMargins(15, 20, 15, 15)

        side_title = QLabel("CONTEXT SETTINGS")
        side_title.setStyleSheet(f"color: {COLORS['primary']}; font-weight: bold; font-size: 12px; margin-bottom: 10px;")
        
        self.screenshot_check = QCheckBox("Screenshots")
        self.screenshot_check.stateChanged.connect(self._update_screenshot_status)
        self.smart_mode_check = QCheckBox("Smart Mode")        

        sidebar_layout.addWidget(side_title)
        sidebar_layout.addWidget(self.screenshot_check)
        sidebar_layout.addWidget(self.smart_mode_check)
        sidebar_layout.addStretch()

        content_area.addLayout(chat_container, 1)
        content_area.addWidget(self.sidebar)
        
        self.main_layout.addLayout(content_area)

        self.timer = QTimer()
        self.timer.timeout.connect(self.update_timer)
        self.elapsed = QTime(0, 0, 0)
        
    def create_action_btn(self, p_type, color_hex, badge):
        text = self.texts[f'{p_type}_btn']
        if badge:
            text = f"{text}  {badge}"
            
        btn = QPushButton(text)
        btn.setFixedHeight(40)
        btn.setCursor(Qt.CursorShape.PointingHandCursor)
        btn.setEnabled(False)
        
        c = QColor(color_hex)
        rgba_hover = f"rgba({c.red()}, {c.green()}, {c.blue()}, 40)"
        
        btn.setStyleSheet(f"""
            QPushButton {{
                background-color: {COLORS['secondary']};
                color: #e2e8f0;
                border: 1px solid {COLORS['border']};
                border-radius: 8px;
                padding: 0 16px;
                font-size: 13px;
                font-weight: 600;
            }}
            QPushButton:hover {{
                border: 1px solid {color_hex};
                background-color: {rgba_hover};
                color: white;
            }}
            QPushButton:pressed {{
                background-color: {color_hex};
                color: black;
            }}
            QPushButton:disabled {{
                background-color: transparent;
                color: {COLORS['text_muted']};
                border: 1px solid {COLORS['border']};
            }}
        """)
        btn.clicked.connect(lambda: self.send_button_prompt(p_type))
        return btn

    def update_btn_style(self, btn, color_hex, is_glow=False):
        border_val = f"1px solid {color_hex}" if is_glow else f"1px solid {COLORS['border']}"
        
        btn.setStyleSheet(f"""
            QPushButton {{
                background-color: {COLORS['bg']};
                color: {color_hex};
                border: {border_val};
                border-radius: 6px;
                font-weight: 800;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background-color: {color_hex};
                color: black;
            }}
        """)
    
    def toggle_sidebar(self):
        is_visible = self.sidebar.isVisible()
        self.sidebar.setVisible(not is_visible)
        self.sidebar_toggle.setText("⚙" if is_visible else "✕")

    def _change_language(self):
        self.current_lang = self.lang_dropdown.currentText()
        self.texts = UI_TEXTS[self.current_lang]
        self.setWindowTitle(self.texts['title'])
        self.start_button.setText(self.texts['start'])
        self.stop_button.setText(self.texts['stop'])
        self.say_button.setText(f"{self.texts['say_btn']} F1")
        self.followup_button.setText(f"{self.texts['followup_btn']} F2")
        self.assist_button.setText(self.texts['assist_btn'])

    def on_start(self):
        if self.started: return
        self.start_button.setEnabled(False)
        self.lang_dropdown.setEnabled(False)
        
        self.signalr_worker = SignalRWorker(self.model_dropdown.currentText())
        self.signalr_worker.chunk_received.connect(self.on_llm_chunk)
        self.signalr_worker.status_received.connect(lambda s: self.add_message(s, False))
        self.signalr_worker.socket_ready.connect(self.on_socket_connected)
        self.signalr_worker.start()

    def on_socket_connected(self):
        selected_lang = self.lang_dropdown.currentText()
        lang = 'ru' if selected_lang == 'ru' else 'en'
        self.signalr_worker.start_audio(lang)
        
        self.mic_thread = AudioCaptureThread(self.signalr_worker, role="me")
        self.mic_thread.start()
        QTimer.singleShot(500, self._start_speaker_thread)
        
        self.started = True
        self.update_ui_state(True)
        self.timer.start(1000)
        self.timer_label.setVisible(True)
    
    def resizeEvent(self, event):
        super().resizeEvent(event)
        if not hasattr(self, 'chat_list'): return
        
        new_width = self.chat_list.viewport().width()
        for i in range(self.chat_list.count()):
            item = self.chat_list.item(i)
            widget = self.chat_list.itemWidget(item)
            if isinstance(widget, ChatMessage):
                widget.update_width(new_width)
                item.setSizeHint(widget.sizeHint())

    def _start_speaker_thread(self):
        if self.started:
            self.speaker_thread = AudioCaptureThread(self.signalr_worker, role="companion")
            self.speaker_thread.start()

    def on_stop(self):
        self.stop_typing()
        if hasattr(self, 'mic_thread') and self.mic_thread:
            self.mic_thread.stop(); self.mic_thread.wait(500)
            self.mic_thread = None
        if hasattr(self, 'speaker_thread') and self.speaker_thread:
            self.speaker_thread.stop(); self.speaker_thread.wait(500)
            self.speaker_thread = None
        if self.signalr_worker:
            self.signalr_worker.stop(); self.signalr_worker.wait(1000)
            self.signalr_worker = None

        self.started = False
        self.update_ui_state(False)
        self.lang_dropdown.setEnabled(True)
        self.timer.stop()
        self.elapsed = QTime(0, 0, 0)
        self.timer_label.setText('00:00:00')
        self.timer_label.setVisible(False)

    def update_ui_state(self, is_started):
        self.start_button.setVisible(not is_started)
        self.start_button.setEnabled(True)
        self.stop_button.setVisible(is_started)
        self.input_box.setEnabled(is_started)
        self.say_button.setEnabled(is_started)
        self.followup_button.setEnabled(is_started)
        self.assist_button.setEnabled(is_started)

    def add_message(self, text, is_user=False):
        current_width = self.chat_list.viewport().width()
        widget = ChatMessage(text, is_user, max_width=current_width)
        
        item = QListWidgetItem()
        item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsSelectable)
        
        self.chat_list.addItem(item)
        self.chat_list.setItemWidget(item, widget)
        
        item.setSizeHint(widget.sizeHint())
        self.chat_list.scrollToBottom()
        return widget

    def update_timer(self):
        self.elapsed = self.elapsed.addSecs(1)
        self.timer_label.setText(self.elapsed.toString('hh:mm:ss'))

    def send_message(self):
        if not self.started: return
        text = self.input_box.toPlainText().strip()
        if not text: return
        self.add_message(text, is_user=True)
        img = self._capture_screenshot()
        self.start_typing()
        self.signalr_worker.invoke_stream("SendMessage", [text, self.model_dropdown.currentText(), img])
        self.input_box.clear()

    def send_button_prompt(self, p_type):
        if not self.started: return
        img = self._capture_screenshot()
        self.signalr_worker.send_screenshot(img)
        self.add_message(self.texts[f'{p_type}_btn'], is_user=True)
        self.start_typing()
        method = {"say": "SendAssistRequest", "followup": "SendFollowupRequest", "assist": "SendMessage"}[p_type]
        args = [self.model_dropdown.currentText(), img]
        if p_type == "assist": args.insert(0, self.texts['assist_btn'])
        self.signalr_worker.invoke_stream(method, args)

    def on_llm_chunk(self, chunk):
        self.stop_typing()

        if chunk.startswith("System:"):
            self.current_stream_msg_widget = None
            self.add_message(chunk, False)
            return
        
        if chunk == "[DONE]":
            self.current_stream_msg_widget = None
            return
            
        if not self.current_stream_msg_widget:
            self.current_stream_msg_widget = self.add_message("", False)
            self.current_stream_text = ""
            
        self.current_stream_text += chunk
        self.current_stream_msg_widget.set_markdown(self.current_stream_text)
        
        item = self.chat_list.item(self.chat_list.count() - 1)
        if item:
            item.setSizeHint(self.current_stream_msg_widget.sizeHint())
            
        self.chat_list.scrollToBottom()

    def start_typing(self):
        if self.typing_item: return
        label = ChatMessage("...", is_user=False)
        self.typing_item = QListWidgetItem()
        self.typing_item.setSizeHint(label.sizeHint())
        self.chat_list.addItem(self.typing_item)
        self.chat_list.setItemWidget(self.typing_item, label)
        self.typing_thread = TypingIndicator()
        self.typing_thread.update_signal.connect(lambda d: label.set_markdown(d))
        self.typing_thread.start()
        self.chat_list.scrollToBottom()

    def stop_typing(self):
        if hasattr(self, 'typing_item') and self.typing_item:
            if hasattr(self, 'typing_thread'):
                self.typing_thread.stop(); self.typing_thread.wait()
            row = self.chat_list.row(self.typing_item)
            if row >= 0: self.chat_list.takeItem(row)
            self.typing_item = None

    def _capture_screenshot(self):
        if self.screenshot_check.isChecked():
            try:
                screenshot = ImageGrab.grab()
                screenshot.thumbnail((1024, 1024))
                buf = io.BytesIO()
                screenshot.save(buf, format="JPEG", quality=70)
                return base64.b64encode(buf.getvalue()).decode("utf-8")
            except: pass
        return None

    def _update_screenshot_status(self):
        if self.signalr_worker:
            self.signalr_worker.screenshots_enabled = self.screenshot_check.isChecked()

    def _set_window_affinity(self):
        if sys.platform == "win32" and SetWindowDisplayAffinity:
            SetWindowDisplayAffinity(self.winId().__int__(), WDA_EXCLUDEFROMCAPTURE)

    def toggle_visibility(self):
        if self.isVisible(): self.hide()
        else: self.show(); self.raise_()

    def closeEvent(self, event):
        self.on_stop()
        QApplication.quit()
        event.accept()

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = ChatWindow('en')
    window.show()
    sys.exit(app.exec())