import io
import base64
import threading
import numpy as np
import pyaudiowpatch as pyaudio
from PIL import ImageGrab
from PyQt6.QtCore import QThread, pyqtSignal
from signalrcore.hub_connection_builder import HubConnectionBuilder
from constants import HUB_URL

audio_init_lock = threading.Lock()

class SignalRWorker(QThread):
    chunk_received = pyqtSignal(str)
    status_received = pyqtSignal(str)
    socket_ready = pyqtSignal()
    
    def __init__(self, model_name):
        super().__init__()
        self.model_name = model_name
        self.connection = None
        self.is_running = True
        self.screenshots_enabled = False
        self._send_lock = threading.Lock()

    def run(self):
        hub_url = HUB_URL.replace("http", "ws", 1) if HUB_URL.startswith("http") else HUB_URL
        self.connection = HubConnectionBuilder()\
            .with_url(hub_url, options={"skip_negotiation": True, "transport": "webSockets"})\
            .with_automatic_reconnect({"type": "raw", "reconnect_interval": 5, "max_attempts": 5})\
            .build()

        self.connection.on_open(self._on_open)
        self.connection.start()

        while self.is_running:
            self.msleep(100)
        
        if self.connection:
            self.connection.stop()

    def _on_open(self):
        self.status_received.emit("System: Socket Connected")
        self.socket_ready.emit()
        threading.Thread(target=self.screenshot_context_loop, daemon=True).start()

    def screenshot_context_loop(self):
        while self.is_running:
            if self.connection and self.is_running and self.screenshots_enabled:
                try:
                    screenshot = ImageGrab.grab()
                    screenshot.thumbnail((1024, 1024))
                    buf = io.BytesIO()
                    screenshot.save(buf, format="JPEG", quality=70)
                    img_str = base64.b64encode(buf.getvalue()).decode("utf-8")
                    if self.is_running:
                        with self._send_lock:
                            self.connection.send("UpdateVisualContext", [img_str])
                except:
                    pass
            threading.Event().wait(2.0)

    def start_audio(self, lang):
        if self.connection and self.is_running: 
            with self._send_lock:
                try:
                    self.connection.send("StartAudio", [lang])
                except:
                    pass

    def stop_audio(self):
        if self.connection: 
            with self._send_lock:
                try: 
                    self.connection.send("StopAudio", [])
                except: 
                    pass

    def send_screenshot(self, img_b64):
        if self.connection and self.is_running:
            with self._send_lock:
                try: 
                    self.connection.send("UpdateVisualContext", [img_b64])
                except: 
                    pass

    def invoke_stream(self, method_name, args):
        if self.connection and self.is_running:
            with self._send_lock:
                try:
                    self.connection.stream(method_name, args).subscribe({
                        "next": lambda chunk: self.chunk_received.emit(str(chunk)),
                        "complete": lambda _: self.chunk_received.emit("[DONE]"),
                        "error": lambda e: self.status_received.emit(f"Stream Error: {e}")
                    })
                except:
                    pass

    def stop(self):
        self.is_running = False
        if self.connection:
            try:
                with self._send_lock:
                    self.connection.send("StopAudio", [])
                self.msleep(500) 
                self.connection.stop()
                self.status_received.emit("System: Stopped")
            except:
                self.status_received.emit("System: Stopped with error")

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