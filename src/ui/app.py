import sys
import keyboard
import io
import base64
from PIL import ImageGrab
from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
                             QPushButton, QComboBox, QCheckBox, QListWidget, 
                             QListWidgetItem, QAbstractItemView, QLabel, QApplication)
from PyQt6.QtCore import Qt, pyqtSignal, QTimer, QTime
from PyQt6.QtGui import QFont

from constants import UI_TEXTS, MODELS, SetWindowDisplayAffinity, WDA_EXCLUDEFROMCAPTURE
from widgets import ChatMessage, ChatInput
from threads import SignalRWorker, AudioCaptureThread, TypingIndicator

class ChatWindow(QMainWindow):
    toggle_signal = pyqtSignal()
    action_signal = pyqtSignal(str)
    
    def __init__(self, current_lang='en'):
        super().__init__()
        self.current_lang = current_lang
        self.texts = UI_TEXTS[self.current_lang]
        self.current_model = MODELS[0]
        self.started = False
        
        self.signalr_worker = None
        self.audio_capture_thread = None
        self.typing_item = None
        self.current_stream_msg_widget = None
        self.current_stream_text = ""

        self.setWindowTitle(self.texts['title'])
        self.setGeometry(50, 50, 600, 700)
        self.setWindowFlags(Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setWindowOpacity(0.9)
        self._set_window_affinity()
        
        self._init_ui()
        
        self.start_button.setEnabled(True)
        
        self.toggle_signal.connect(self.toggle_visibility)
        self.action_signal.connect(self.send_button_prompt)
        
        keyboard.add_hotkey('ctrl+/', lambda: self.toggle_signal.emit())
        keyboard.add_hotkey('f1', lambda: self.action_signal.emit('say'))
        keyboard.add_hotkey('f2', lambda: self.action_signal.emit('followup'))
    
    def closeEvent(self, event):
        self.on_stop()
        QApplication.quit()
        event.accept()

    def on_start(self):
        if self.started: return
        self.start_button.setEnabled(False)
        self.lang_dropdown.setEnabled(False)
        self.start_button.setText("...")
        
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

        self.speaker_thread = AudioCaptureThread(self.signalr_worker, role="companion")
        self.speaker_thread.start()
        
        self.started = True
        self.start_button.setText(self.texts['start'])
        self.update_ui_state(True)
        self.timer.start(1000)
        self.timer_label.setVisible(True)

    def on_stop(self):
        self.stop_typing()
        
        if hasattr(self, 'mic_thread') and self.mic_thread:
            self.mic_thread.stop()
            self.mic_thread.wait(500)
            self.mic_thread = None

        if hasattr(self, 'speaker_thread') and self.speaker_thread:
            self.speaker_thread.stop()
            self.speaker_thread.wait(500)
            self.speaker_thread = None

        if self.signalr_worker:
            self.signalr_worker.stop()
            self.signalr_worker.wait(1000)
            self.signalr_worker = None

        self.started = False
        self.update_ui_state(False)
        self.lang_dropdown.setEnabled(True)
        self.timer.stop()
        self.elapsed = QTime(0, 0, 0) # Сброс таймера
        self.timer_label.setText('00:00:00')
        self.timer_label.setVisible(False)

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
        if chunk == "[DONE]":
            self.current_stream_msg_widget = None
            return
        if not self.current_stream_msg_widget:
            self.current_stream_msg_widget = self.add_message("", False)
            self.current_stream_text = ""
        self.current_stream_text += chunk
        self.current_stream_msg_widget.set_markdown(self.current_stream_text)
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
                self.typing_thread.stop()
                self.typing_thread.wait()
            row = self.chat_list.row(self.typing_item)
            if row >= 0:
                self.chat_list.takeItem(row)
            self.typing_item = None

    def add_message(self, text, is_user=False):
        widget = ChatMessage(text, is_user)
        item = QListWidgetItem()
        item.setSizeHint(widget.sizeHint())
        self.chat_list.addItem(item)
        self.chat_list.setItemWidget(item, widget)
        self.chat_list.scrollToBottom()
        return widget

    def update_ui_state(self, is_started):
        self.start_button.setVisible(not is_started)
        self.start_button.setEnabled(True)
        self.stop_button.setVisible(is_started)
        self.input_box.setEnabled(is_started)
        self.say_button.setEnabled(is_started)
        self.followup_button.setEnabled(is_started)
        self.assist_button.setEnabled(is_started)
        self.smart_button.setEnabled(is_started)
    
    def _update_screenshot_status(self):
        if self.signalr_worker:
            self.signalr_worker.screenshots_enabled = self.screenshot_check.isChecked()

    def _init_ui(self):
        central_widget = QWidget()
        central_widget.setStyleSheet("background: rgba(20,20,20,0.99);")
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(5,5,5,5)

        top_layout = QHBoxLayout()
        self.lang_label = QLabel(self.texts['lang_label'])
        self.lang_dropdown = QComboBox()
        self.lang_dropdown.addItems(['ru','en'])
        self.lang_dropdown.setCurrentText(self.current_lang)
        top_layout.addWidget(self.lang_label)
        top_layout.addWidget(self.lang_dropdown)

        center_box = QHBoxLayout()
        center_box.addStretch()
        self.start_button = QPushButton(self.texts['start'])
        self.start_button.clicked.connect(self.on_start)
        self.start_button.setFixedSize(60, 24)
        self.start_button.setStyleSheet("QPushButton{background-color: rgba(50,150,50,0.99); color:#FFFFFF; border-radius:6px;} QPushButton:hover{background-color: rgba(70,180,70,1);}")
        
        self.stop_button = QPushButton(self.texts['stop'])
        self.stop_button.clicked.connect(self.on_stop)
        self.stop_button.setFixedSize(60, 24)
        self.stop_button.setStyleSheet("QPushButton{background-color: rgba(200,0,0,0.99); color:#FFFFFF; border-radius:6px;} QPushButton:hover{background-color: rgba(230,30,30,1);}")
        self.stop_button.setVisible(False)
        center_box.addWidget(self.start_button)
        center_box.addWidget(self.stop_button)
        center_box.addStretch()
        top_layout.addLayout(center_box)

        self.screenshot_check = QCheckBox("Screenshots")
        self.screenshot_check.stateChanged.connect(self._update_screenshot_status)
        self.screenshot_check.setStyleSheet("color: #AAAAAA; margin-right: 10px;")
        top_layout.addWidget(self.screenshot_check)
        
        self.smart_button = QPushButton(self.texts['smart_btn'])
        self.smart_button.setCheckable(True)
        self.smart_button.setStyleSheet("QPushButton{background-color: rgba(80,80,80,0.5); color:#AAAAAA; border-radius:4px; padding:2px 8px;} QPushButton:checked{background-color: rgba(100,200,100,0.8); color: white;}")
        top_layout.addWidget(self.smart_button)
        
        self.timer_label = QLabel('00:00:00')
        self.timer_label.setStyleSheet("color: white;")
        self.timer_label.setVisible(False)
        top_layout.addWidget(self.timer_label)
        main_layout.addLayout(top_layout)

        self.chat_list = QListWidget()
        self.chat_list.setStyleSheet("background: transparent; border: none;")
        self.chat_list.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerPixel)
        self.chat_list.setSelectionMode(QAbstractItemView.SelectionMode.NoSelection)
        self.chat_list.setSpacing(5)
        main_layout.addWidget(self.chat_list)

        self.input_box = ChatInput()
        self.input_box.setFixedHeight(50)
        self.input_box.setFont(QFont("Segoe UI", 10))
        self.input_box.setStyleSheet("background-color: rgba(30,30,30,0.6); color:#E0E0E0; border-radius:4px;")
        self.input_box.send_signal.connect(self.send_message)
        self.input_box.setEnabled(False)
        main_layout.addWidget(self.input_box)

        button_layout = QHBoxLayout()
        style_btn = "QPushButton{background-color: rgba(50,50,50,0.6); color:#E0E0E0; border-radius:4px; padding:6px;} QPushButton:hover{background-color: rgba(90,90,90,0.9);}" 
        self.say_button = QPushButton(self.texts['say_btn']); self.say_button.setStyleSheet(style_btn); self.say_button.setEnabled(False)
        self.say_button.clicked.connect(lambda: self.send_button_prompt('say'))
        self.followup_button = QPushButton(self.texts['followup_btn']); self.followup_button.setStyleSheet(style_btn); self.followup_button.setEnabled(False)
        self.followup_button.clicked.connect(lambda: self.send_button_prompt('followup'))
        self.assist_button = QPushButton(self.texts['assist_btn']); self.assist_button.setStyleSheet(style_btn); self.assist_button.setEnabled(False)
        self.assist_button.clicked.connect(lambda: self.send_button_prompt('assist'))
        button_layout.addWidget(self.say_button); button_layout.addWidget(self.followup_button); button_layout.addWidget(self.assist_button)
        main_layout.addLayout(button_layout)

        self.model_dropdown = QComboBox()
        self.model_dropdown.addItems(MODELS)
        main_layout.addWidget(self.model_dropdown)
        
        self.timer = QTimer(); self.timer.timeout.connect(self.update_timer)
        self.elapsed = QTime(0,0,0)

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

    def _set_window_affinity(self):
        if sys.platform == "win32" and SetWindowDisplayAffinity:
            SetWindowDisplayAffinity(self.winId().__int__(), WDA_EXCLUDEFROMCAPTURE)

    def toggle_visibility(self):
        if self.isVisible(): self.hide()
        else: self.show(); self.raise_()

    def update_timer(self):
        self.elapsed = self.elapsed.addSecs(1)
        self.timer_label.setText(self.elapsed.toString('hh:mm:ss'))

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = ChatWindow('en')
    window.show()
    sys.exit(app.exec())