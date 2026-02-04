import io
import base64
import threading
import pyaudio
from PIL import ImageGrab
from PyQt6.QtCore import QThread, pyqtSignal
from signalrcore.hub_connection_builder import HubConnectionBuilder
from constants import HUB_URL

class SignalRWorker(QThread):
    chunk_received = pyqtSignal(str)
    status_received = pyqtSignal(str)
    socket_ready = pyqtSignal()
    
    def __init__(self, model_name):
        super().__init__()
        self.model_name = model_name
        self.connection = None
        self.is_running = True

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

    def _on_open(self):
        self.status_received.emit("System: Socket Connected")
        self.socket_ready.emit()
        threading.Thread(target=self.screenshot_context_loop, daemon=True).start()

    def screenshot_context_loop(self):
        while self.is_running:
            if self.connection:
                try:
                    screenshot = ImageGrab.grab()
                    screenshot.thumbnail((1024, 1024))
                    buf = io.BytesIO()
                    screenshot.save(buf, format="JPEG", quality=70)
                    img_str = base64.b64encode(buf.getvalue()).decode("utf-8")
                    self.connection.send("UpdateVisualContext", [img_str])
                except:
                    pass
            threading.Event().wait(2.0)

    def start_audio(self, lang):
        if self.connection: self.connection.send("StartAudio", [lang])

    def stop_audio(self):
        if self.connection: self.connection.send("StopAudio", [])

    def send_audio_chunk(self, chunk, role):
        if self.connection: self.connection.send("SendAudioChunk", [list(chunk), role])

    def send_screenshot(self, img_b64):
        if self.connection: self.connection.send("UpdateVisualContext", [img_b64])

    def invoke_stream(self, method_name, args):
        self.connection.stream(method_name, args).subscribe({
            "next": lambda chunk: self.chunk_received.emit(str(chunk)),
            "complete": lambda: self.chunk_received.emit("[DONE]"),
            "error": lambda e: self.status_received.emit(f"Stream Error: {e}")
        })

    def stop(self):
        self.is_running = False
        if self.connection: self.connection.stop()

class AudioCaptureThread(QThread):
    def __init__(self, worker, role="me"):
        super().__init__()
        self.worker = worker
        self.role = role
        self.is_running = True
        self.chunk_size = 1024
        self.rate = 16000 

    def run(self):
        p = pyaudio.PyAudio()
        stream = p.open(format=pyaudio.paInt16, channels=1,
                        rate=self.rate, input=True,
                        frames_per_buffer=self.chunk_size)

        while self.is_running:
            try:
                data = stream.read(self.chunk_size, exception_on_overflow=False)
                if data:
                    self.worker.send_audio_chunk(data, self.role)
            except:
                pass

        stream.stop_stream()
        stream.close()
        p.terminate()

    def stop(self):
        self.is_running = False

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