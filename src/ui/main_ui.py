import os
import sys
import requests
from PyQt6.QtWidgets import QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QTextEdit, QLabel, QScrollArea, QFrame, QPushButton, QSizePolicy, QComboBox
from PyQt6.QtCore import Qt, pyqtSignal, QThread, QTimer, QTime
from PyQt6.QtGui import QFont, QKeyEvent
from dotenv import load_dotenv
import ctypes
import re
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
    'ru': {'title': 'AI-Суфлер', 'start': 'Start', 'stop': 'Stop', 'say_btn': 'Что сказать', 'followup_btn': 'Уточнить', 'assist_btn': 'Помощь с последним вопросом', 'lang_label': 'Язык', 'smart_btn': 'Smart Mode'},
    'en': {'title': 'AI Copilot', 'start': 'Start', 'stop': 'Stop', 'say_btn': 'What to say', 'followup_btn': 'Follow up', 'assist_btn': 'Assist', 'lang_label': 'Lang', 'smart_btn': 'Smart Mode'}
}

MODELS = ["Grok (grok-4)", "OpenAI GPT-4.1 Mini"]

class SignalRWorker(QThread):
    response_received = pyqtSignal(str)
    status_received = pyqtSignal(str)
    
    def __init__(self, model_name):
        super().__init__()
        self.model_name = model_name
        self.connection = None

    def run(self):
        try:
            self.connection = HubConnectionBuilder()\
                .with_url(HUB_URL)\
                .build()

            self.connection.on("ReceiveResponse", self.handle_response)
            self.connection.on("ReceiveStatus", self.handle_status)
            self.connection.on("ReceiveError", self.handle_status)
            
            self.connection.start()
            self.connection.send("ActivateSmartMode", [self.model_name])
            
            # Keep thread alive
            while self.connection and self.connection.transport.state == 1:
                self.msleep(100)
                
        except Exception as e:
            self.status_received.emit(f"SignalR Error: {e}")

    def handle_response(self, args):
        if args and len(args) > 0:
            self.response_received.emit(args[0])

    def handle_status(self, args):
        if args and len(args) > 0:
            self.status_received.emit(f"System: {args[0]}")

    def stop(self):
        if self.connection:
            self.connection.stop()
            self.connection = None

class BackendWorker(QThread):
    finished_signal = pyqtSignal(str)
    def __init__(self, user_text, model):
        super().__init__()
        self.user_text = user_text
        self.model = model
    def run(self):
        try:
            resp = requests.post(f"{BACKEND_URL}/message", json={"Text": self.user_text, "Model": self.model}, timeout=10)
            if resp.status_code == 200:
                response_text = resp.json().get("response", "No response")
            else:
                response_text = f"Error: {resp.status_code}"
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
            result = resp.json() if resp.headers.get('Content-Type','').startswith('application/json') else {}
            result['status_code'] = resp.status_code
        except Exception as e:
            result = {'status_code': 0, 'error': str(e)}
        self.finished_signal.emit(result)

class ChatMessage(QFrame):
    def __init__(self, text, is_user=False):
        super().__init__()
        self.setFrameShape(QFrame.Shape.NoFrame)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(5,2,5,2)
        h_layout = QHBoxLayout()
        label = QLabel()
        label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        label.setWordWrap(True)
        label.setFont(QFont("Segoe UI", 10))
        label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        self.set_markdown(label, text)
        if is_user:
            h_layout.addStretch()
            h_layout.addWidget(label)
            label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            label.setStyleSheet("color:#E0E0E0; background-color: rgba(100,150,255,0.2); padding:4px; border-radius:4px;")
        else:
            h_layout.addWidget(label)
            label.setStyleSheet("color:#E0E0E0; background-color: rgba(40,40,40,0.6); padding:4px; border-radius:4px;")
        layout.addLayout(h_layout)
    def set_markdown(self, label: QLabel, text: str):
        def repl_code(match):
            code_text = match.group(2)
            code_text = code_text.replace('<','&lt;').replace('>','&gt;')
            return f'<pre style="background:#2E2E2E; color:#F0F0F0; padding:5px; border-radius:4px;"><code>{code_text}</code></pre>'
        md_text = re.sub(r'```(\w*)\n([\s\S]*?)```', repl_code, text)
        md_text = md_text.replace('\n','<br>')
        label.setText(md_text)

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
        self.smart_mode_active = False
        self.threads = []
        self.signalr_worker = None
        self.setWindowTitle(self.texts['title'])
        self.setGeometry(50, 50, 600, 700)
        self.setWindowFlags(Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setWindowOpacity(0.9)
        self._set_window_affinity()
        self._init_ui()

    def closeEvent(self, event):
        self.input_box.send_signal.disconnect()
        if self.signalr_worker:
            self.signalr_worker.stop()
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
        center_box.addWidget(self.start_button)
        self.stop_button = QPushButton(self.texts['stop'])
        self.stop_button.clicked.connect(self.on_stop)
        self.stop_button.setFixedSize(60, 24)
        self.stop_button.setStyleSheet("QPushButton{background-color: rgba(200,0,0,0.99); color:#FFFFFF; border-radius:6px;} QPushButton:hover{background-color: rgba(230,30,30,1);}")
        self.stop_button.setVisible(False)
        center_box.addWidget(self.stop_button)
        center_box.addStretch()
        top_layout.addLayout(center_box)

        self.timer_label = QLabel('00:00:00')
        self.timer_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.timer_label.setVisible(False)
        top_layout.addWidget(self.timer_label)

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
        
        self.smart_button = QPushButton(self.texts['smart_btn'])
        self.smart_button.clicked.connect(self.toggle_smart_mode)
        self.smart_button.setStyleSheet(style_btn)
        self.smart_button.setCheckable(True)
        self.smart_button.setEnabled(False)
        button_layout.addWidget(self.smart_button)

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

    def send_button_prompt(self, prompt_type):
        prompt_map = {
            'say': self.texts['say_btn'],
            'followup': self.texts['followup_btn'],
            'assist': self.texts['assist_btn']
        }
        text = prompt_map.get(prompt_type, '')
        if not text:
            return

        self.add_message(text, is_user=True)
        self.send_to_backend(text)

    def send_message(self):
        text = self.input_box.toPlainText().strip()
        if not text:
            return
        self.add_message(text, is_user=True)
        self.input_box.clear()
        self.send_to_backend(text)

    def send_to_backend(self, user_text):
        current_model = self.model_dropdown.currentText()
        
        self.typing_label = ChatMessage("...", is_user=False)
        self.chat_layout.addWidget(self.typing_label)
        self.scroll_area.verticalScrollBar().setValue(self.scroll_area.verticalScrollBar().maximum())
        
        self.typing_thread = TypingIndicator()
        self.threads.append(self.typing_thread)
        self.typing_thread.update_signal.connect(lambda dots: self.typing_label.set_markdown(self.typing_label.findChild(QLabel), dots))
        self.typing_thread.start()

        worker = BackendWorker(user_text, current_model)
        self.threads.append(worker)
        def handle_response(resp_text):
            self.typing_thread.stop()
            self.chat_layout.removeWidget(self.typing_label)
            self.typing_label.deleteLater()
            self.add_message(resp_text, is_user=False)
        worker.finished_signal.connect(handle_response)
        worker.start()

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
        self.start_button.setText(self.texts['start'])
        self.stop_button.setText(self.texts['stop'])
        self.lang_label.setText(self.texts['lang_label'])
        self.smart_button.setText(self.texts['smart_btn'])

    def on_model_change(self, index):
        self.current_model = self.model_dropdown.currentText()

    def on_start(self):
        if self.started:
            return
        lang = 'ru' if self.current_lang == 'ru' else 'en'
        self.audio_worker = AudioWorker("start", lang)
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self.handle_start_response)
        self.audio_worker.start()

    def handle_start_response(self, result):
        if result.get('status') != 'started':
            self.add_message(f"Error: failed to start audio service", is_user=False)
            return
        self.started = True
        self.lang_dropdown.setEnabled(False)
        self.start_button.setVisible(False)
        self.stop_button.setVisible(True)
        self.input_box.setEnabled(True)
        self.say_button.setEnabled(True)
        self.smart_button.setEnabled(True)
        self.elapsed = QTime(0,0,0)
        self.timer_label.setText('00:00:00')
        self.timer_label.setVisible(True)
        self.timer.start(1000)

        # Auto-activate smart mode logic if needed here
        if self.smart_button.isChecked():
            self.toggle_smart_mode()

    def on_stop(self):
        if not self.started:
            return
        if self.signalr_worker:
            self.signalr_worker.stop()
        self.audio_worker = AudioWorker("stop")
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self.handle_stop_response)
        self.audio_worker.start()

    def handle_stop_response(self, result):
        self.started = False
        self.lang_dropdown.setEnabled(True)
        self.start_button.setVisible(True)
        self.stop_button.setVisible(False)
        self.input_box.setEnabled(False)
        self.say_button.setEnabled(False)
        self.smart_button.setEnabled(False)
        self.timer.stop()
        self.timer_label.setVisible(False)

    def toggle_smart_mode(self):
        if self.smart_button.isChecked():
            self.smart_button.setStyleSheet("background-color: rgba(100,200,100,0.6);")
            self.add_message("Smart Mode Initialized...", is_user=False)
            
            self.signalr_worker = SignalRWorker(self.current_model)
            self.signalr_worker.response_received.connect(lambda text: self.add_message(text, False))
            self.signalr_worker.status_received.connect(lambda text: self.add_message(text, False))
            self.signalr_worker.start()
        else:
            self.smart_button.setStyleSheet("QPushButton{background-color: rgba(50,50,50,0.6); color:#E0E0E0; border-radius:4px; padding:6px;}")
            if self.signalr_worker:
                self.signalr_worker.stop()
                self.signalr_worker = None
            self.add_message("Smart Mode Deactivated.", is_user=False)

    def update_timer(self):
        self.elapsed = self.elapsed.addSecs(1)
        self.timer_label.setText(self.elapsed.toString('hh:mm:ss'))

class TypingIndicator(QThread):
    update_signal = pyqtSignal(str)
    def __init__(self):
        super().__init__()
        self.running = True
    def run(self):
        dots = 1
        while self.running:
            self.update_signal.emit('.'*dots)
            dots = (dots % 3) + 1
            self.msleep(500)
    def stop(self):
        self.running = False

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