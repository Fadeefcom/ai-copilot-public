import requests
import os

class CopilotClient:
    def __init__(self, base_url):
        self.base_url = base_url

    def send_message(self, text, model):
        try:
            resp = requests.post(
                f"{self.base_url}/api/message", 
                json={"Text": text, "Model": model}, 
                timeout=30
            )
            if resp.status_code == 200:
                return resp.json().get("response", "No response content")
            return f"Error: {resp.status_code}"
        except Exception as e:
            return f"Connection error: {e}"

    def start_audio(self, lang):
        try:
            resp = requests.post(f"{self.base_url}/api/audio/start", params={"language": lang}, timeout=5)
            return resp.status_code == 200
        except:
            return False

    def stop_audio(self):
        try:
            requests.post(f"{self.base_url}/api/audio/stop", timeout=5)
            return True
        except:
            return False