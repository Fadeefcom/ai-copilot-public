import os
import sys
import ctypes
import re
import requests
from dotenv import load_dotenv

from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
    QTextEdit, QLabel, QScrollArea, QFrame, QPushButton, 
    QSizePolicy, QComboBox
)
from PyQt6.QtCore import Qt, pyqtSignal, QThread, QTimer, QTime
from PyQt6.QtGui import QFont, QKeyEvent

# --- 1. CONFIGURATION & CONSTANTS ---

load_dotenv()
BACKEND_URL = os.getenv("BACKEND_URL", "http://localhost:5019")

WDA_EXCLUDEFROMCAPTURE = 0x00000011
SetWindowDisplayAffinity = None
if sys.platform == "win32":
    try:
        ctypes.windll.user32.SetWindowDisplayAffinity.argtypes = [ctypes.c_void_p, ctypes.c_uint]
        ctypes.windll.user32.SetWindowDisplayAffinity.restype = ctypes.c_bool
        SetWindowDisplayAffinity = ctypes.windll.user32.SetWindowDisplayAffinity
    except AttributeError:
        pass

# Текстовые ресурсы
UI_TEXTS = {
    'ru': {
        'title': 'AI-Суфлер', 
        'start': 'Start', 
        'stop': 'Stop', 
        'say_btn': 'Что сказать', 
        'followup_btn': 'Уточнить', 
        'assist_btn': 'Помощь с последним вопросом', 
        'lang_label': 'Язык'
    },
    'en': {
        'title': 'AI Copilot', 
        'start': 'Start', 
        'stop': 'Stop', 
        'say_btn': 'What to say', 
        'followup_btn': 'Follow up', 
        'assist_btn': 'Assist', 
        'lang_label': 'Lang'
    }
}

MODELS = ["OpenAI GPT-4.1 Mini", "Grok (grok-4)"]

UI_STYLES = {
    "window_bg": "background: rgba(20,20,20,0.99);",
    "btn_start": "QPushButton{background-color: rgba(50,150,50,0.99); color:#FFFFFF; border-radius:6px;} QPushButton:hover{background-color: rgba(70,180,70,1);}",
    "btn_stop": "QPushButton{background-color: rgba(200,0,0,0.99); color:#FFFFFF; border-radius:6px;} QPushButton:hover{background-color: rgba(230,30,30,1);}",
    "btn_action": "QPushButton{background-color: rgba(50,50,50,0.6); color:#E0E0E0; border-radius:4px; padding:6px;} QPushButton:hover{background-color: rgba(90,90,90,0.9);}",
    "input_box": "background-color: rgba(30,30,30,0.6); color:#E0E0E0; border-radius:4px;",
    "msg_user": "color:#E0E0E0; background-color: rgba(100,150,255,0.2); padding:4px; border-radius:4px;",
    "msg_ai": "color:#E0E0E0; background-color: rgba(40,40,40,0.6); padding:4px; border-radius:4px;",
    "code_block": "background:#2E2E2E; color:#F0F0F0; padding:5px; border-radius:4px;"
}

# --- 2. API CLIENT LAYER ---

class CopilotClient:
    """Encapsulates all HTTP communication with the backend."""
    def __init__(self, base_url):
        self.base_url = base_url

    def send_message(self, text, model):
        """Sends a text prompt to the AI."""
        try:
            url = f"{self.base_url}/api/message"
            payload = {"Text": text, "Model": model}
            resp = requests.post(url, json=payload, timeout=30)
            
            if resp.status_code == 200:
                return resp.json().get("response", "No response field")
            return f"Error {resp.status_code}: {resp.text}"
        except Exception as e:
            return f"Connection error: {e}"

    def control_audio(self, command, lang=None):
        """Controls audio service (start/stop)."""
        try:
            if command == "start":
                url = f"{self.base_url}/api/audio/start"
                params = {"language": lang}
                resp = requests.post(url, params=params, timeout=5)
            else:
                url = f"{self.base_url}/api/audio/stop"
                resp = requests.post(url, timeout=5)
            
            # Обработка ответа
            if resp.headers.get('Content-Type','').startswith('application/json'):
                data = resp.json()
                data['status_code'] = resp.status_code
                return data
            return {'status_code': resp.status_code, 'status': 'unknown'}
        except Exception as e:
            return {'status_code': 0, 'error': str(e)}

# --- 3. WORKERS (THREADING) ---

class MessageWorker(QThread):
    finished_signal = pyqtSignal(str)

    def __init__(self, client: CopilotClient, text: str, model: str):
        super().__init__()
        self.client = client
        self.text = text
        self.model = model

    def run(self):
        response = self.client.send_message(self.text, self.model)
        self.finished_signal.emit(response)

class AudioWorker(QThread):
    finished_signal = pyqtSignal(dict)

    def __init__(self, client: CopilotClient, action: str, lang: str = None):
        super().__init__()
        self.client = client
        self.action = action
        self.lang = lang

    def run(self):
        result = self.client.control_audio(self.action, self.lang)
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

# --- 4. UI COMPONENTS ---

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
        
        style = UI_STYLES["msg_user"] if is_user else UI_STYLES["msg_ai"]
        self.label.setStyleSheet(style)

        if is_user:
            h_layout.addStretch()
            h_layout.addWidget(self.label)
            self.label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        else:
            h_layout.addWidget(self.label)
            h_layout.addStretch()

        layout.addLayout(h_layout)

    def set_markdown(self, text: str):
        def repl_code(match):
            code_text = match.group(2).replace('<', '&lt;').replace('>', '&gt;')
            return f'<pre style="{UI_STYLES["code_block"]}"><code>{code_text}</code></pre>'
        
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

# --- 5. MAIN WINDOW ---

class ChatWindow(QMainWindow):
    def __init__(self, current_lang='en'):
        super().__init__()
        self.client = CopilotClient(BACKEND_URL)
        
        self.current_lang = current_lang
        self.texts = UI_TEXTS[self.current_lang]
        self.started = False
        self.threads = []
        
        self._setup_window_properties()
        self._init_ui()

    def _setup_window_properties(self):
        self.setWindowTitle(self.texts['title'])
        self.setGeometry(50, 50, 600, 700)
        self.setWindowFlags(Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setWindowOpacity(0.9)
        
        if sys.platform == "win32" and SetWindowDisplayAffinity:
            hwnd = self.winId().__int__()
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)

    def _init_ui(self):
        central_widget = QWidget()
        central_widget.setStyleSheet(UI_STYLES["window_bg"])
        self.setCentralWidget(central_widget)
        
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(5, 5, 5, 5)

        # --- Top Bar ---
        top_layout = QHBoxLayout()
        
        # Left: Lang
        left_box = QHBoxLayout()
        self.lang_label = QLabel(self.texts['lang_label'])
        self.lang_dropdown = QComboBox()
        self.lang_dropdown.addItems(['ru', 'en'])
        self.lang_dropdown.setCurrentText(self.current_lang)
        self.lang_dropdown.currentTextChanged.connect(self.on_lang_change)
        left_box.addWidget(self.lang_label)
        left_box.addWidget(self.lang_dropdown)
        top_layout.addLayout(left_box)

        # Center: Start/Stop
        center_box = QHBoxLayout()
        center_box.addStretch()
        
        self.start_button = QPushButton(self.texts['start'])
        self.start_button.setFixedSize(60, 24)
        self.start_button.setStyleSheet(UI_STYLES["btn_start"])
        self.start_button.clicked.connect(self.on_start)
        
        self.stop_button = QPushButton(self.texts['stop'])
        self.stop_button.setFixedSize(60, 24)
        self.stop_button.setStyleSheet(UI_STYLES["btn_stop"])
        self.stop_button.clicked.connect(self.on_stop)
        self.stop_button.setVisible(False)
        
        center_box.addWidget(self.start_button)
        center_box.addWidget(self.stop_button)
        center_box.addStretch()
        top_layout.addLayout(center_box)

        # Right: Timer
        self.timer_label = QLabel('00:00:00')
        self.timer_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.timer_label.setVisible(False)
        top_layout.addWidget(self.timer_label)

        main_layout.addLayout(top_layout)

        # --- Chat Area ---
        self.scroll_area = QScrollArea()
        self.scroll_area.setWidgetResizable(True)
        self.scroll_area.setStyleSheet("background: transparent; border: none;")
        
        self.chat_container = QWidget()
        self.chat_layout = QVBoxLayout(self.chat_container)
        self.chat_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.scroll_area.setWidget(self.chat_container)
        
        main_layout.addWidget(self.scroll_area)

        # --- Input ---
        self.input_box = ChatInput()
        self.input_box.setFixedHeight(50)
        self.input_box.setFont(QFont("Segoe UI", 10))
        self.input_box.setStyleSheet(UI_STYLES["input_box"])
        self.input_box.send_signal.connect(self.send_user_message)
        self.input_box.setEnabled(False)
        main_layout.addWidget(self.input_box)

        # --- Action Buttons ---
        button_layout = QHBoxLayout()
        self.say_button = self._create_action_btn('say_btn', 'say')
        self.followup_button = self._create_action_btn('followup_btn', 'followup')
        self.assist_button = self._create_action_btn('assist_btn', 'assist')
        
        button_layout.addWidget(self.say_button)
        button_layout.addWidget(self.followup_button)
        button_layout.addWidget(self.assist_button)
        main_layout.addLayout(button_layout)

        # --- Bottom: Model ---
        bottom_layout = QHBoxLayout()
        self.model_dropdown = QComboBox()
        self.model_dropdown.addItems(MODELS)
        bottom_layout.addWidget(self.model_dropdown)
        bottom_layout.addStretch()
        main_layout.addLayout(bottom_layout)

        # --- Logic Helpers ---
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_timer)
        self.elapsed_time = QTime(0, 0, 0)
        self.typing_indicator = None

    def _create_action_btn(self, text_key, action_type):
        btn = QPushButton(self.texts[text_key])
        btn.clicked.connect(lambda: self.send_prompt_action(action_type))
        btn.setStyleSheet(UI_STYLES["btn_action"])
        btn.setEnabled(False)
        return btn

    # --- Event Handlers ---

    def on_lang_change(self, text):
        if self.started:
            # Revert if running (to avoid state complexity)
            self.lang_dropdown.blockSignals(True)
            self.lang_dropdown.setCurrentText(self.current_lang)
            self.lang_dropdown.blockSignals(False)
            return
            
        self.current_lang = text
        self.texts = UI_TEXTS[self.current_lang]
        self.setWindowTitle(self.texts['title'])
        self._update_ui_texts()

    def _update_ui_texts(self):
        self.say_button.setText(self.texts['say_btn'])
        self.followup_button.setText(self.texts['followup_btn'])
        self.assist_button.setText(self.texts['assist_btn'])
        self.start_button.setText(self.texts['start'])
        self.stop_button.setText(self.texts['stop'])
        self.lang_label.setText(self.texts['lang_label'])

    def on_start(self):
        if self.started: return
        
        lang_code = 'ru' if self.current_lang == 'ru' else 'en'
        
        self.audio_worker = AudioWorker(self.client, "start", lang_code)
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self._handle_start_result)
        self.audio_worker.start()

    def _handle_start_result(self, result):
        if result.get('status') != 'started':
            self.add_message("Error: Failed to start audio service", is_user=False)
            return

        self.started = True
        self._set_ui_state(running=True)
        
        self.elapsed_time = QTime(0, 0, 0)
        self.timer_label.setText('00:00:00')
        self.timer_label.setVisible(True)
        self.timer.start(1000)

    def on_stop(self):
        if not self.started: return
        
        self.audio_worker = AudioWorker(self.client, "stop")
        self.threads.append(self.audio_worker)
        self.audio_worker.finished_signal.connect(self._handle_stop_result)
        self.audio_worker.start()

    def _handle_stop_result(self, result):
        if result.get('status') != 'stopped':
            self.add_message("Error: Failed to stop audio service cleanly", is_user=False)
        
        self.started = False
        self._set_ui_state(running=False)
        
        self.timer.stop()
        self.timer_label.setVisible(False)

    def _set_ui_state(self, running):
        self.lang_dropdown.setEnabled(not running)
        self.start_button.setVisible(not running)
        self.stop_button.setVisible(running)
        
        self.input_box.setEnabled(running)
        self.say_button.setEnabled(running)
        self.followup_button.setEnabled(running)
        self.assist_button.setEnabled(running)

    def update_timer(self):
        self.elapsed_time = self.elapsed_time.addSecs(1)
        self.timer_label.setText(self.elapsed_time.toString('hh:mm:ss'))

    def add_message(self, text, is_user=False):
        msg = ChatMessage(text, is_user)
        self.chat_layout.addWidget(msg)
        QApplication.processEvents()
        self._scroll_to_bottom()

    def _scroll_to_bottom(self):
        sb = self.scroll_area.verticalScrollBar()
        sb.setValue(sb.maximum())

    def send_user_message(self):
        text = self.input_box.toPlainText().strip()
        if not text: return
        
        self.add_message(text, is_user=True)
        self.input_box.clear()
        self._execute_api_request(text)

    def send_prompt_action(self, action_type):
        prompt_map = {
            'say': self.texts['say_btn'],
            'followup': self.texts['followup_btn'],
            'assist': self.texts['assist_btn']
        }
        text = prompt_map.get(action_type, '')
        if not text: return
        
        self.add_message(text, is_user=True)
        self._execute_api_request(text)

    def _execute_api_request(self, text):
        # 1. Show Typing Indicator
        self.typing_label = ChatMessage("...", is_user=False)
        self.chat_layout.addWidget(self.typing_label)
        self._scroll_to_bottom()

        self.typing_indicator = TypingIndicator()
        self.threads.append(self.typing_indicator)
        self.typing_indicator.update_signal.connect(
            lambda dots: self.typing_label.set_markdown(dots)
        )
        self.typing_indicator.start()

        model = self.model_dropdown.currentText()
        worker = MessageWorker(self.client, text, model)
        self.threads.append(worker)
        worker.finished_signal.connect(self._on_message_response)
        worker.start()

    def _on_message_response(self, response_text):
        if self.typing_indicator:
            self.typing_indicator.stop()
            self.typing_indicator.wait()
        
        self.chat_layout.removeWidget(self.typing_label)
        self.typing_label.deleteLater()
        self.typing_label = None

        self.add_message(response_text, is_user=False)

    def closeEvent(self, event):
        for thread in self.threads:
            if thread.isRunning():
                if hasattr(thread, 'stop'): thread.stop()
                thread.quit()
                thread.wait()
        event.accept()

# --- 6. ENTRY POINT ---

def main():
    app = QApplication(sys.argv)
    window = ChatWindow('en')
    window.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()