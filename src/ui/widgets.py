import re
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QTextEdit, 
                             QLabel, QFrame, QSizePolicy)
from PyQt6.QtCore import Qt, pyqtSignal, QSize, QPropertyAnimation, QEasingCurve
from PyQt6.QtGui import QFont, QKeyEvent
from constants import COLORS

class ChatMessage(QFrame):
    def __init__(self, text, is_user=False, max_width=None):
        super().__init__()
        self.setFrameShape(QFrame.Shape.NoFrame)
        self.is_user = is_user
        self.is_system = text.startswith("System:")
        
        self._layout = QVBoxLayout(self)
        self._layout.setSpacing(0)
        
        if self.is_user:
            self._layout.setContentsMargins(0, 0, 20, 0)
        else:
            self._layout.setContentsMargins(0, 0, 0, 0)

        h_layout = QHBoxLayout()
        h_layout.setSpacing(0)
        h_layout.setContentsMargins(0, 0, 0, 0)

        self.label = QLabel()
        self.label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.label.setWordWrap(True)
        self.label.setFont(QFont("Segoe UI", 10))
        self.label.setTextFormat(Qt.TextFormat.RichText)
        
        if self.is_system:
            bg_color = "rgba(39, 39, 42, 40)"
            style = f"color: {COLORS['text_muted']}; background-color: {bg_color}; border-radius: 0px; font-size: 11px; font-style: italic;"
            self.label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
            h_layout.addWidget(self.label, alignment=Qt.AlignmentFlag.AlignLeft)
        elif is_user:
            bg_color = "rgba(0, 229, 255, 40)" 
            style = f"color: #FFFFFF; background-color: {bg_color}; border-radius: 0px; padding: 5px;"
            h_layout.addStretch()
            h_layout.addWidget(self.label, alignment=Qt.AlignmentFlag.AlignRight)
        else:
            bg_color = "rgba(168, 85, 247, 40)"
            style = f"color: #FFFFFF; background-color: {bg_color}; border-radius: 0px; padding: 5px;"
            self.label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
            h_layout.addWidget(self.label, alignment=Qt.AlignmentFlag.AlignLeft)

        self.label.setStyleSheet(style)
        self.set_markdown(text)
        self._layout.addLayout(h_layout)
        
        if max_width:
            self.update_width(max_width)

        self.anim = QPropertyAnimation(self, b"windowOpacity")
        self.anim.setDuration(300)
        self.anim.setStartValue(0.0)
        self.anim.setEndValue(1.0)
        self.anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self.anim.start()

    def set_markdown(self, text: str):
        if not text:
            self.label.setText("")
            return
        def repl_code(match):
            code_text = match.group(2).replace('<', '&lt;').replace('>', '&gt;')
            return f'<pre style="background:rgba(0,0,0,0.3); color:#FFFFFF; border-radius:0; padding: 5px;"><code>{code_text}</code></pre>'
        md_text = re.sub(r'```(\w*)\n([\s\S]*?)```', repl_code, text)
        md_text = md_text.replace('\n', '<br>')
        md_text = re.sub(r'\*\*(.*?)\*\*', r'<b>\1</b>', md_text)
        md_text = re.sub(r'\*(.*?)\*', r'<i>\1</i>', md_text)
        self.label.setText(md_text)

    def sizeHint(self):
        self.label.adjustSize()
        extra = 10 if self.is_system else (10 if self.is_user else 35)
        return QSize(self.label.width(), self.label.heightForWidth(self.label.width()) + extra)
    
    def update_width(self, new_width):
        self.label.setMinimumWidth(0)
        self.label.setMaximumWidth(16777215)

        if self.is_user:
            self.label.setMaximumWidth(int(new_width * 0.75))
        else:
            self.label.setFixedWidth(new_width)
        
        self.label.adjustSize()
        self.updateGeometry()

class ChatInput(QTextEdit):
    send_signal = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setStyleSheet(f"color: #FFFFFF; background-color: {COLORS['secondary']};")
    
    def insertFromMimeData(self, source):
        self.insertPlainText(source.text())

    def keyPressEvent(self, event: QKeyEvent):
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter) and not (event.modifiers() & Qt.KeyboardModifier.ShiftModifier):
            self.send_signal.emit()
            event.accept()
        else:
            super().keyPressEvent(event)