import os
import sys
import requests
import ctypes
import re
from dotenv import load_dotenv

from PyQt6.QtWidgets import (QApplication, QMainWindow, QWidget, QVBoxLayout, 
                             QHBoxLayout, QTextEdit, QLabel, QScrollArea, QFrame, 
                             QPushButton, QSizePolicy, QComboBox)
from PyQt6.QtCore import Qt, pyqtSignal, QThread, QTimer, QTime
from PyQt6.QtGui import QFont, QKeyEvent

import logging
from signalrcore.hub_connection_builder import HubConnectionBuilder

load_dotenv()
BACKEND_URL = os.getenv("BACKEND_URL", "http://localhost:57875/api")
HUB_URL = os.getenv("HUB_URL", "http://localhost:57875/hubs/smart")

WDA_EXCLUDEFROMCAPTURE = 0x00000011
SetWindowDisplayAffinity = None
if sys.platform == "win32":
    try:
        ctypes.windll.user32.SetWindowDisplayAffinity.argtypes = [ctypes.c_void_p, ctypes.c_uint]
        ctypes.windll.user32.SetWindowDisplayAffinity.restype = ctypes.c_bool
        SetWindowDisplayAffinity = ctypes.windll.user32.SetWindowDisplayAffinity
    except AttributeError:
        pass

UI_TEXTS = {
    'ru': {
        'title': 'AI-Суфлер', 
        'start': 'Start', 
        'stop': 'Stop', 
        'say_btn': 'Что сказать', 
        'followup_btn': 'Уточнить', 
        'assist_btn': 'Помощь с последним вопросом', 
        'lang_label': 'Язык', 
        'smart_btn': 'Smart Mode'
    },
    'en': {
        'title': 'AI Copilot', 
        'start': 'Start', 
        'stop': 'Stop', 
        'say_btn': 'What to say', 
        'followup_btn': 'Follow up', 
        'assist_btn': 'Assist', 
        'lang_label': 'Lang', 
        'smart_btn': 'Smart Mode'
    }
}

MODELS = ["Grok grok-4", "OpenAI gpt-4.1-mini", "OpenAI gpt-5-nano", "OpenAI gpt-5-mini"]

class SignalRWorker(QThread):
    chunk_received = pyqtSignal(str)
    status_received = pyqtSignal(str)
    
    def __init__(self, model_name):
        super().__init__()
        self.model_name = model_name
        self.connection = None
        self.is_running = True

    def run(self):
        try:
            hub_url = HUB_URL
            # Ensure we use WebSocket protocol
            if hub_url.startswith("http"):
                hub_url = hub_url.replace("http", "ws", 1)

            self.connection = HubConnectionBuilder()\
                .with_url(hub_url, options={
                    "skip_negotiation": True,
                    "transport": "webSockets"
                })\
                .with_automatic_reconnect({
                    "type": "raw",
                    "keep_alive_interval": 10,
                    "reconnect_interval": 5,
                    "max_attempts": 5
                })\
                .build()

            # 1. DEFINE THE STREAMING LOGIC AS A SEPARATE FUNCTION
            def start_streaming():
                self.status_received.emit("System: Smart Mode Connected.")
                print(f"DEBUG: Connection Open. Starting stream with: {self.model_name}")
                
                try:
                    self.connection.stream("StreamSmartMode", [str(self.model_name)])\
                        .subscribe({
                            "next": self.handle_stream_item,
                            "complete": lambda: self.status_received.emit("System: Stream session ended."),
                            "error": lambda x: self.status_received.emit(f"System Error: {x}")
                        })
                except Exception as stream_err:
                    self.status_received.emit(f"Stream Start Error: {stream_err}")

            # 2. ATTACH IT TO ON_OPEN
            # The library calls this ONLY when the socket is truly ready
            self.connection.on_open(start_streaming)
            
            self.connection.on_close(lambda: self.status_received.emit("System: Smart Mode Disconnected."))
            
            # 3. START THE CONNECTION
            print("DEBUG: Starting connection...")
            self.connection.start()

            # Keep thread alive
            while self.connection and self.is_running:
                self.msleep(100)

        except Exception as e:
            self.status_received.emit(f"SignalR Connection Error: {e}")

    def handle_stream_item(self, item):
        self.chunk_received.emit(str(item))

    def stop(self):
        self.is_running = False
        if self.connection:
            try:
                self.connection.stop()
            except:
                pass
            self.connection = None

class BackendWorker(QThread):
    finished_signal = pyqtSignal(str)
    
    def __init__(self, endpoint, payload):
        super().__init__()
        self.endpoint = endpoint
        self.payload = payload
        
    def run(self):
        try:
            url = f"{BACKEND_URL}{self.endpoint}"
            
            resp = requests.post(
                url, 
                json=self.payload, 
                timeout=10
            )
            
            if resp.status_code == 200:
                response_text = resp.json().get("response", "No response")
            else:
                response_text = f"Error: {resp.status_code} - {resp.text}"
        except Exception as e:
            response_text = f"Request error: {e}"
            
        self.finished_signal.emit(response_text)

class AudioWorker(QThread):
    finished_signal = pyqtSignal(dict)
    
    def __init__(self, action, lang=None):
        super().__init__()
        self.action = action
        self.lang = lang
        
    def run(self):
        try:
            if self.action == "start":
                resp = requests.post(f"{BACKEND_URL}/audio/start", params={"language": self.lang}, timeout=5)
            elif self.action == "stop":
                resp = requests.post(f"{BACKEND_URL}/audio/stop", timeout=5)
            else:
                return
            
            content_type = resp.headers.get('Content-Type', '')
            if content_type.startswith('application/json'):
                result = resp.json()
            else:
                result = {}
            result['status_code'] = resp.status_code
            
        except Exception as e:
            result = {'status_code': 0, 'error': str(e)}
        self.finished_signal.emit(result)

class TypingIndicator(QThread):
    update_signal = pyqtSignal(str)
    
    def __init__(self):
        super().__init__()
        self.running = True
        
    def run(self):
        dots = 1
        while self.running:
            self.update_signal.emit('.' * dots)
            dots = (dots % 3) + 1
            self.msleep(500)
            
    def stop(self):
        self.running = False

class ChatMessage(QFrame):
    def __init__(self, text, is_user=False):
        super().__init__()
        self.setFrameShape(QFrame.Shape.NoFrame)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(5, 2, 5, 2)
        
        h_layout = QHBoxLayout()
        self.label = QLabel()
        self.label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.label.setWordWrap(True)
        self.label.setFont(QFont("Segoe UI", 10))
        self.label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        self.set_markdown(text)
        
        if is_user:
            h_layout.addStretch()
            h_layout.addWidget(self.label)
            self.label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            self.label.setStyleSheet("color:#E0E0E0; background-color: rgba(100,150,255,0.2); padding:4px; border-radius:4px;")
        else:
            h_layout.addWidget(self.label)
            self.label.setStyleSheet("color:#E0E0E0; background-color: rgba(40,40,40,0.6); padding:4px; border-radius:4px;")
        
        layout.addLayout(h_layout)
        
    def set_markdown(self, text: str):
        def repl_code(match):
            code_text = match.group(2)
            code_text = code_text.replace('<', '&lt;').replace('>', '&gt;')
            return f'<pre style="background:#2E2E2E; color:#F0F0F0; padding:5px; border-radius:4px;"><code>{code_text}</code></pre>'
        
        md_text = re.sub(r'```(\w*)\n([\s\S]*?)```', repl_code, text)
        md_text = md_text.replace('\n', '<br>')
        self.label.setText(md_text)

class ChatInput(QTextEdit):
    send_signal = pyqtSignal()
    
    def keyPressEvent(self, event: QKeyEvent):
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter) and not (event.modifiers() & Qt.KeyboardModifier.ShiftModifier):
            self.send_signal.emit()
            event.accept()
        else:
            super().keyPressEvent(event)

class ChatWindow(QMainWindow):
    def __init__(self, current_lang='en'):
        super().__init__()
        self.current_lang = current_lang
        self.texts = UI_TEXTS[self.current_lang]
        self.current_model = MODELS[0]
        self.started = False
        
        self.threads = []
        self.signalr_worker = None
        
        self.current_stream_msg_widget = None
        self.current_stream_text = ""

        self.setWindowTitle(self.texts['title'])
        self.setGeometry(50, 50, 600, 700)
        self.setWindowFlags(Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setWindowOpacity(0.9)
        self._set_window_affinity()
        
        self._init_ui()

    def closeEvent(self, event):
        try:
            self.input_box.send_signal.disconnect()
        except:
            pass

        if self.signalr_worker:
            if hasattr(self.signalr_worker, 'stop'):
                self.signalr_worker.stop()
            self.signalr_worker.quit()
            self.signalr_worker.wait()

        for thread in self.threads:
            if thread.isRunning():
                if hasattr(thread, 'stop'):
                    thread.stop()
                thread.quit()
                thread.wait()
        
        event.accept()
        QApplication.quit()

    def _set_window_affinity(self):
        if sys.platform == "win32" and SetWindowDisplayAffinity:
            hwnd = self.winId().__int__()
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)

    def _init_ui(self):
        central_widget = QWidget()
        central_widget.setStyleSheet("background: rgba(20,20,20,0.99);")
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(5,5,5,5)

        top_layout = QHBoxLayout()
        
        left_box = QHBoxLayout()
        self.lang_label = QLabel(self.texts['lang_label'])
        self.lang_dropdown = QComboBox()
        self.lang_dropdown.addItems(['ru','en'])
        self.lang_dropdown.setCurrentText(self.current_lang)
        self.lang_dropdown.currentTextChanged.connect(self.on_lang_dropdown_change)
        left_box.addWidget(self.lang_label)
        left_box.addWidget(self.lang_dropdown)
        top_layout.addLayout(left_box)

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

        right_box = QHBoxLayout()
        
        self.smart_button = QPushButton(self.texts['smart_btn'])
        self.smart_button.setCheckable(True)
        self.smart_button.clicked.connect(self.toggle_smart_mode)
        self.smart_button.setStyleSheet("QPushButton{background-color: rgba(80,80,80,0.5); color:#AAAAAA; border-radius:4px; padding:2px 8px;} QPushButton:checked{background-color: rgba(100,200,100,0.8); color: white;}")
        self.smart_button.setEnabled(False) 
        
        self.timer_label = QLabel('00:00:00')
        self.timer_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.timer_label.setVisible(False)

        right_box.addWidget(self.smart_button)
        right_box.addWidget(self.timer_label)
        top_layout.addLayout(right_box)

        main_layout.addLayout(top_layout)

        self.scroll_area = QScrollArea()
        self.scroll_area.setWidgetResizable(True)
        self.scroll_area.setStyleSheet("background: transparent; border: none;")
        
        self.chat_container = QWidget()
        self.chat_layout = QVBoxLayout(self.chat_container)
        self.chat_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.scroll_area.setWidget(self.chat_container)
        
        main_layout.addWidget(self.scroll_area)

        self.input_box = ChatInput()
        self.input_box.setFixedHeight(50)
        self.input_box.setFont(QFont("Segoe UI", 10))
        self.input_box.setStyleSheet("background-color: rgba(30,30,30,0.6); color:#E0E0E0; border-radius:4px;")
        self.input_box.send_signal.connect(self.send_message)
        self.input_box.setEnabled(False)
        main_layout.addWidget(self.input_box)

        button_layout = QHBoxLayout()
        style_btn = "QPushButton{background-color: rgba(50,50,50,0.6); color:#E0E0E0; border-radius:4px; padding:6px;} QPushButton:hover{background-color: rgba(90,90,90,0.9);}" 
        
        self.say_button = QPushButton(self.texts['say_btn'])
        self.say_button.clicked.connect(lambda: self.send_button_prompt('say'))
        self.say_button.setStyleSheet(style_btn)
        self.say_button.setEnabled(False)
        button_layout.addWidget(self.say_button)
        
        self.followup_button = QPushButton(self.texts['followup_btn'])
        self.followup_button.clicked.connect(lambda: self.send_button_prompt('followup'))
        self.followup_button.setStyleSheet(style_btn)
        self.followup_button.setEnabled(False)
        button_layout.addWidget(self.followup_button)
        
        self.assist_button = QPushButton(self.texts['assist_btn'])
        self.assist_button.clicked.connect(lambda: self.send_button_prompt('assist'))
        self.assist_button.setStyleSheet(style_btn)
        self.assist_button.setEnabled(False)
        button_layout.addWidget(self.assist_button)
        
        main_layout.addLayout(button_layout)

        bottom_layout = QHBoxLayout()
        self.model_dropdown = QComboBox()
        self.model_dropdown.addItems(MODELS)
        self.model_dropdown.currentIndexChanged.connect(self.on_model_change)
        bottom_layout.addWidget(self.model_dropdown)
        bottom_layout.addStretch()
        main_layout.addLayout(bottom_layout)

        self.timer = QTimer()
        self.timer.timeout.connect(self.update_timer)
        self.elapsed = QTime(0,0,0)

    def add_message(self, text, is_user=False):
        msg = ChatMessage(text, is_user)
        self.chat_layout.addWidget(msg)
        QApplication.processEvents()
        self.scroll_area.verticalScrollBar().setValue(self.scroll_area.verticalScrollBar().maximum())
        return msg

    def _initiate_request(self, endpoint, payload):
        self.typing_label = ChatMessage("...", is_user=False)
        self.chat_layout.addWidget(self.typing_label)
        self.scroll_area.verticalScrollBar().setValue(self.scroll_area.verticalScrollBar().maximum())
        
        self.typing_thread = TypingIndicator()
        self.threads.append(self.typing_thread)
        self.typing_thread.update_signal.connect(lambda dots: self.typing_label.set_markdown(dots))
        self.typing_thread.start()

        worker = BackendWorker(endpoint, payload)
        self.threads.append(worker)
        
        def handle_response(resp_text):
            self.typing_thread.stop()
            try:
                self.chat_layout.removeWidget(self.typing_label)
                self.typing_label.deleteLater()
            except:
                pass
            self.add_message(resp_text, is_user=False)
            
        worker.finished_signal.connect(handle_response)
        worker.start()

    def send_to_backend(self, user_text):
        current_model = self.model_dropdown.currentText()
        self._initiate_request("/message", {"Text": user_text, "Model": current_model})

    def send_message(self):
        text = self.input_box.toPlainText().strip()
        if not text:
            return
        self.add_message(text, is_user=True)
        self.input_box.clear()
        self.send_to_backend(text)

    def send_button_prompt(self, prompt_type):
        current_model = self.model_dropdown.currentText()
        
        if prompt_type == 'say':
            self._initiate_request("/assist", {"Model": current_model})
            
        elif prompt_type == 'followup':
            self._initiate_request("/followup", {"Model": current_model})
            
        elif prompt_type == 'assist':
            text = self.texts['assist_btn']
            self.add_message(text, is_user=True)
            self.send_to_backend(text)

    def on_start(self):
        if self.started:
            return
        
        self.start_button.setEnabled(False)
        self.start_button.setText("...")

        lang = 'ru' if self.current_lang == 'ru' else 'en'
        self.audio_worker = AudioWorker("start", lang)
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self.handle_start_response)
        self.audio_worker.start()

    def handle_start_response(self, result):
        if result.get('status') != 'started':
            self.add_message(f"Error: failed to start audio service ({result.get('error','')})", is_user=False)
            self.start_button.setEnabled(True)
            self.start_button.setText(self.texts['start'])
            return

        self.started = True
        self.lang_dropdown.setEnabled(False)
        
        self.start_button.setVisible(False)
        self.start_button.setEnabled(True)
        self.start_button.setText(self.texts['start']) 
        
        self.stop_button.setVisible(True)

        self.input_box.setEnabled(True)
        self.say_button.setEnabled(True)
        self.followup_button.setEnabled(True)
        self.assist_button.setEnabled(True)
        self.smart_button.setEnabled(True)

        self.elapsed = QTime(0,0,0)
        self.timer_label.setText('00:00:00')
        self.timer_label.setVisible(True)
        self.timer.start(1000)
        
        if self.smart_button.isChecked():
            self.toggle_smart_mode()

    def on_stop(self):
        if not self.started:
            return
        
        if self.signalr_worker:
            self.signalr_worker.stop()
            self.signalr_worker = None
        
        if self.smart_button.isChecked():
            self.smart_button.setChecked(False)
            self.smart_button.setStyleSheet("QPushButton{background-color: rgba(80,80,80,0.5); color:#AAAAAA; border-radius:4px; padding:2px 8px;} QPushButton:checked{background-color: rgba(100,200,100,0.8); color: white;}")

        self.audio_worker = AudioWorker("stop")
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self.handle_stop_response)
        self.audio_worker.start()

    def handle_stop_response(self, result):
        if result.get('status') != 'stopped':
            self.add_message(f"Error: failed to stop audio service", is_user=False)

        self.started = False
        self.lang_dropdown.setEnabled(True)
        
        self.start_button.setVisible(True)
        self.stop_button.setVisible(False)
        
        self.input_box.setEnabled(False)
        self.say_button.setEnabled(False)
        self.followup_button.setEnabled(False)
        self.assist_button.setEnabled(False)
        self.smart_button.setEnabled(False)

        self.timer.stop()
        self.timer_label.setVisible(False)

    def toggle_smart_mode(self):
        if self.smart_button.isChecked():
            self.add_message("Smart Mode Initialized...", is_user=False)
            
            if not self.signalr_worker:
                self.signalr_worker = SignalRWorker(self.current_model)
                self.signalr_worker.chunk_received.connect(self.process_stream_chunk)
                self.signalr_worker.status_received.connect(lambda s: self.add_message(s, False))
                self.signalr_worker.start()
        else:
            if self.signalr_worker:
                self.signalr_worker.stop()
                self.signalr_worker = None
                self.add_message("Smart Mode Deactivated.", is_user=False)
            
            self.current_stream_msg_widget = None
            self.current_stream_text = ""

    def process_stream_chunk(self, chunk):
        if chunk.startswith("Smart Mode:") or chunk.startswith("System:"):
            self.add_message(chunk, False)
            return

        if chunk.startswith("[System] Question detected:"):
            self.add_message(chunk, False)
            self.current_stream_msg_widget = self.add_message("...", False)
            self.current_stream_text = ""
            return

        if chunk.startswith("[System] Response complete"):
            self.current_stream_msg_widget = None
            self.current_stream_text = ""
            return

        if self.current_stream_msg_widget:
            self.current_stream_text += chunk
            self.current_stream_msg_widget.set_markdown(self.current_stream_text)
            self.scroll_area.verticalScrollBar().setValue(self.scroll_area.verticalScrollBar().maximum())

    def on_lang_dropdown_change(self, text):
        if self.started:
            try:
                self.lang_dropdown.blockSignals(True)
                self.lang_dropdown.setCurrentText(self.current_lang)
            finally:
                self.lang_dropdown.blockSignals(False)
            return
        
        self.current_lang = text
        self.texts = UI_TEXTS[self.current_lang]
        self.setWindowTitle(self.texts['title'])
        self.update_button_texts()

    def update_button_texts(self):
        self.say_button.setText(self.texts['say_btn'])
        self.followup_button.setText(self.texts['followup_btn'])
        self.assist_button.setText(self.texts['assist_btn'])
        self.start_button.setText(self.texts['start'])
        self.stop_button.setText(self.texts['stop'])
        self.lang_label.setText(self.texts['lang_label'])
        self.smart_button.setText(self.texts['smart_btn'])

    def on_model_change(self, index):
        self.current_model = self.model_dropdown.currentText()

    def update_timer(self):
        self.elapsed = self.elapsed.addSecs(1)
        self.timer_label.setText(self.elapsed.toString('hh:mm:ss'))

def start_ui_loop(current_lang='en'):
    app = QApplication.instance()
    if app is None:
        app = QApplication(sys.argv)
    main_window = ChatWindow(current_lang)
    main_window.show()
    return app, main_window

if __name__ == "__main__":
    app, window = start_ui_loop('en')
    sys.exit(app.exec())