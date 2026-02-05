import os
import sys
import ctypes
from dotenv import load_dotenv

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

COLORS = {
    "bg": "#09090b",
    "card": "#101014",
    "primary": "#00e5ff",      # Cyan
    "secondary": "#27272a",    # Dark Gray
    "accent": "#a855f7",       # Purple
    "success": "#22c55e",      # Green
    "destructive": "#ef4444",  # Red
    "warning": "#f59e0b",      # Orange
    "border": "#27272a",
    "text_muted": "#b0b0b6"
}

MODELS = [
    "Azure thinking",  # Выберет grok-4-fast-reasoning
    "Azure fast",      # Выберет gpt-4o-mini
]