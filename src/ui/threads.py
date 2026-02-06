import io
import base64
import threading
import requests
import numpy as np
import pyaudiowpatch as pyaudio
from PIL import ImageGrab
from PyQt6.QtCore import QThread, pyqtSignal
from signalrcore.hub_connection_builder import HubConnectionBuilder
from urllib.parse import urlparse, parse_qs
from constants import BACKEND_URL, HUB_URL

audio_init_lock = threading.Lock()

class SignalRWorker(QThread):
    chunk_received = pyqtSignal(str)
    status_received = pyqtSignal(str)
    socket_ready = pyqtSignal()
    
    def __init__(self, model_name):
        super().__init__()
        self.model_name = model_name
        self.connection = None
        self.is_running = False
        self.screenshots_enabled = False
        self._send_lock = threading.Lock()

    def run(self):
        hub_url = HUB_URL
        self.connection = HubConnectionBuilder()\
            .with_url(hub_url, options={"transport": "webSockets"})\
            .with_automatic_reconnect({"type": "raw", "reconnect_interval": 5, "max_attempts": 5})\
            .build()

        self.connection.on_open(self._on_open)
        self.connection.on_close(self._handle_disconnect)

        try:
            self.connection.start()
            self.is_running = True 
        except Exception as e:
            print(f"[DEBUG] Connection failed to start: {e}")
            self.is_running = False
            self.connection_failed.emit()
            return

        while self.is_running:
            self.msleep(100)
            if self.connection and self.connection.transport.state.value == 0:
                self.is_running = False
        
        if self.connection:
            self.connection.stop()

    def _on_open(self):
        self.status_received.emit("System: Socket Connected")
        self.socket_ready.emit()
        threading.Thread(target=self.screenshot_context_loop, daemon=True).start()
    
    def _handle_disconnect(self):
        if self.is_running:
            print("[DEBUG] Connection lost unexpectedly!")
            self.is_running = False
            self.status_received.emit("System: Connection Lost")

    def screenshot_context_loop(self):
        while self.is_running:
            if self.connection and self.is_running and self.screenshots_enabled:
                conn_id = None
                try:
                    transport_url = getattr(self.connection.transport, 'url', '')
                    parsed_url = urlparse(transport_url)
                    params = parse_qs(parsed_url.query)
                    if 'id' in params:
                        conn_id = params['id'][0]
                except Exception as e:
                    print(f"[DEBUG] Error parsing ID from URL: {e}")

                if not conn_id:
                    threading.Event().wait(1.0)
                    continue

                try:
                    screenshot = ImageGrab.grab()
                    screenshot.thumbnail((800, 800))
                    buf = io.BytesIO()
                    screenshot.save(buf, format="JPEG", quality=60)
                    img_str = base64.b64encode(buf.getvalue()).decode("utf-8")

                    payload = {
                        "connectionId": conn_id,
                        "base64Image": img_str
                    }

                    response = requests.post(
                        f"{BACKEND_URL}/context/screenshot", 
                        json=payload,
                        timeout=5
                    )
                    
                    if response.status_code != 200:
                        print(f"REST Upload failed: {response.status_code}")

                except Exception as e:
                    print(f"REST Screenshot error: {e}")
            
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
                    self.is_running = False
                except: 
                    pass

    def send_audio_chunk(self, chunk, role):
        if self.connection and self.is_running:
            with self._send_lock:
                try:
                    encoded = base64.b64encode(chunk).decode('utf-8')
                    self.connection.send("SendAudioChunk", [encoded, role])
                except Exception as e:
                    print(f"Send error: {e}")

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

class AudioCaptureThread(QThread):
    def __init__(self, worker, role="me"):
        super().__init__()
        self.worker = worker
        self.role = role
        self.is_running = True
        self.chunk_size = 2048

    def run(self):
        p = None
        stream = None
        native_rate = 0
        channels = 0
        
        with audio_init_lock:
            try:
                p = pyaudio.PyAudio()
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
                    if p: p.terminate()
                    return

                native_rate = int(device_info['defaultSampleRate'])
                channels = int(device_info['maxInputChannels'])
                
                stream = p.open(
                    format=pyaudio.paInt16,
                    channels=channels,
                    rate=native_rate,
                    input=True,
                    input_device_index=device_info['index'],
                    frames_per_buffer=self.chunk_size
                )
            except Exception as e:
                print(f"Failed to init {self.role}: {e}")
                if p: p.terminate()
                return

        try:
            target_rate = 24000
            while self.is_running:
                try:
                    raw_data = stream.read(self.chunk_size, exception_on_overflow=False)
                    if not raw_data:
                        continue
                    
                    audio_np = np.frombuffer(raw_data, dtype=np.int16).copy()
                    if channels > 1: 
                        audio_np = audio_np.reshape(-1, channels).mean(axis=1).astype(np.int16)
                    
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
                except:
                    continue
        finally:
            if stream:
                stream.stop_stream()
                stream.close()
            if p:
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