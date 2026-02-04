import io
import base64
import threading
import numpy as np
import pyaudiowpatch as pyaudio
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
        self.screenshots_enabled = False

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
                        self.connection.send("UpdateVisualContext", [img_str])
                except:
                    pass
            threading.Event().wait(2.0)

    def start_audio(self, lang):
        if self.connection and self.is_running: 
            self.connection.send("StartAudio", [lang])

    def stop_audio(self):
        if self.connection: 
            try: self.connection.send("StopAudio", [])
            except: pass

    def send_audio_chunk(self, chunk, role):
        if self.connection and self.is_running:
            try:
                encoded = base64.b64encode(chunk).decode('utf-8')
                self.connection.send("SendAudioChunk", [encoded, role])
            except:
                pass

    def send_screenshot(self, img_b64):
        if self.connection and self.is_running:
            try: self.connection.send("UpdateVisualContext", [img_b64])
            except: pass

    def invoke_stream(self, method_name, args):
        if self.connection and self.is_running:
            self.connection.stream(method_name, args).subscribe({
                "next": lambda chunk: self.chunk_received.emit(str(chunk)),
                "complete": lambda _: self.chunk_received.emit("[DONE]"),
                "error": lambda e: self.status_received.emit(f"Stream Error: {e}")
            })

    def stop(self):
        self.is_running = False
        
        if self.connection:
            try:
                self.connection.send("StopAudio", [])
                self.msleep(200) 
                self.connection.stop()
            except:
                pass
        
        self.status_received.emit("System: Socket Disconnected")

class AudioCaptureThread(QThread):
    def __init__(self, worker, role="me"):
        super().__init__()
        self.worker = worker
        self.role = role
        self.is_running = True
        self.chunk_size = 2048

    def run(self):
        p = pyaudio.PyAudio()
        try:
            device_info = None
            if self.role == "me":
                device_info = p.get_default_input_device_info()
            else:
                wasapi_info = next((p.get_host_api_info_by_index(i) 
                                   for i in range(p.get_host_api_count()) 
                                   if p.get_host_api_info_by_index(i)['type'] == pyaudio.paWASAPI), None)
                if wasapi_info:
                    default_out = p.get_device_info_by_index(wasapi_info['defaultOutputDevice'])
                    for i in range(p.get_device_count()):
                        dev = p.get_device_info_by_index(i)
                        if dev["isLoopbackDevice"] and default_out["name"] in dev["name"]:
                            device_info = dev
                            break

            if not device_info:
                return

            native_rate = int(device_info['defaultSampleRate'])
            channels = int(device_info['maxInputChannels'])
            target_rate = 16000
            
            stream = p.open(
                format=pyaudio.paInt16,
                channels=channels,
                rate=native_rate,
                input=True,
                input_device_index=device_info['index'],
                frames_per_buffer=self.chunk_size
            )

            while self.is_running:
                try:
                    raw_data = stream.read(self.chunk_size, exception_on_overflow=False)
                    if not raw_data:
                        continue
                    
                    audio_np = np.frombuffer(raw_data, dtype=np.int16).copy()
                    
                    if channels > 1:
                        audio_np = audio_np[::channels]
                    
                    if np.abs(audio_np).max() == 0:
                        continue

                    if native_rate != target_rate:
                        duration = len(audio_np) / native_rate
                        num_target_samples = int(duration * target_rate)                        
                        audio_resampled = np.interp(
                            np.linspace(0, 1, num_target_samples),
                            np.linspace(0, 1, len(audio_np)),
                            audio_np
                        ).astype(np.int16)
                        
                        self.worker.send_audio_chunk(audio_resampled.tobytes(), self.role)
                    else:
                        self.worker.send_audio_chunk(audio_np.tobytes(), self.role)
                except Exception as e:
                    print(f"Capture error: {e}")
                    continue

            stream.stop_stream()
            stream.close()
        finally:
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