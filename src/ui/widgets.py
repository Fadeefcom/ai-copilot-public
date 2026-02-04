import re
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QTextEdit, 
                             QLabel, QFrame, QSizePolicy)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont, QKeyEvent

class ChatMessage(QFrame):
    def __init__(self, text, is_user=False):
        super().__init__()
        self.setFrameShape(QFrame.Shape.NoFrame)
        self._layout = QVBoxLayout(self)
        self._layout.setContentsMargins(5, 2, 5, 2)
        
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
        
        self._layout.addLayout(h_layout)
        
    def set_markdown(self, text: str):
        def repl_code(match):
            code_text = match.group(2)
            code_text = code_text.replace('<', '&lt;').replace('>', '&gt;')
            return f'<pre style="background:#2E2E2E; color:#F0F0F0; padding:5px; border-radius:4px;"><code>{code_text}</code></pre>'
        
        md_text = re.sub(r'```(\w*)\n([\s\S]*?)```', repl_code, text)
        md_text = md_text.replace('\n', '<br>')
        self.label.setText(md_text)

    def sizeHint(self):
        return self._layout.sizeHint()

class ChatInput(QTextEdit):
    send_signal = pyqtSignal()
    
    def keyPressEvent(self, event: QKeyEvent):
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter) and not (event.modifiers() & Qt.KeyboardModifier.ShiftModifier):
            self.send_signal.emit()
            event.accept()
        else:
            super().keyPressEvent(event)