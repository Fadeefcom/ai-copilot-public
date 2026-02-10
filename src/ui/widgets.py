import re
from PyQt6.QtWidgets import (QStyledItemDelegate, QTextEdit)
from PyQt6.QtCore import Qt, pyqtSignal, QAbstractListModel, QModelIndex, QSize, QRectF
from PyQt6.QtGui import QTextDocument, QAbstractTextDocumentLayout, QPalette, QColor, QPainter, QKeyEvent
from constants import COLORS

class ChatModel(QAbstractListModel):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.messages = []

    def rowCount(self, parent=QModelIndex()):
        return len(self.messages)

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid() or not (0 <= index.row() < len(self.messages)):
            return None
        
        msg = self.messages[index.row()]
        
        if role == Qt.ItemDataRole.DisplayRole:
            return msg['text']
        if role == Qt.ItemDataRole.UserRole:
            return msg
        
        return None

    def add_message(self, text, is_user, is_system=False):
        self.beginInsertRows(QModelIndex(), len(self.messages), len(self.messages))
        self.messages.append({
            'text': text,
            'is_user': is_user,
            'is_system': is_system
        })
        self.endInsertRows()

    def update_last_message(self, new_text):
        if not self.messages: return
        idx = len(self.messages) - 1
        self.messages[idx]['text'] = new_text
        self.dataChanged.emit(self.index(idx), self.index(idx))

    def remove_last_message(self):
        if not self.messages: return
        idx = len(self.messages) - 1
        self.beginRemoveRows(QModelIndex(), idx, idx)
        self.messages.pop()
        self.endRemoveRows()

class ChatDelegate(QStyledItemDelegate):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.doc = QTextDocument()

    def paint(self, painter, option, index):
        painter.save()
        
        msg_data = index.data(Qt.ItemDataRole.UserRole)
        text = msg_data['text']
        is_user = msg_data['is_user']
        is_system = msg_data['is_system']

        rect = option.rect
        
        max_width = int(rect.width() * 0.85) if not is_system else rect.width()
        self._setup_document(text, max_width)
        
        content_width = self.doc.idealWidth()
        content_height = self.doc.size().height()
        
        bubble_rect = QRectF(0, rect.y(), content_width + 20, content_height + 20)
        
        if is_user:
            bubble_rect.moveRight(rect.right() - 10)
            c = QColor(COLORS['primary'])
            bg_color = QColor(c.red(), c.green(), c.blue(), 40)
        elif is_system:
            bubble_rect.setWidth(rect.width())
            bg_color = QColor(39, 39, 42, 40)
        else:
            bubble_rect.moveLeft(rect.left() + 10)
            c = QColor(COLORS['accent'])
            bg_color = QColor(c.red(), c.green(), c.blue(), 40)

        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(bg_color)
        painter.drawRoundedRect(bubble_rect, 8, 8)

        painter.translate(bubble_rect.left() + 10, bubble_rect.top() + 10)
        
        ctx = QAbstractTextDocumentLayout.PaintContext()
        ctx.palette.setColor(QPalette.ColorRole.Text, Qt.GlobalColor.white)
        
        self.doc.documentLayout().draw(painter, ctx)
        
        painter.restore()

    def sizeHint(self, option, index):
        msg_data = index.data(Qt.ItemDataRole.UserRole)
        if not msg_data: return QSize(0,0)
        
        width = option.rect.width() if option.rect.width() > 0 else 400
        max_width = int(width * 0.85) if not msg_data['is_system'] else width
        
        self._setup_document(msg_data['text'], max_width)
        return QSize(width, int(self.doc.size().height()) + 20)

    def _setup_document(self, text, width):
        html = self._markdown_to_html(text)
        self.doc.setHtml(f"<div style='color: #fff; font-family: Segoe UI; font-size: 13px;'>{html}</div>")
        self.doc.setTextWidth(width)

    def _markdown_to_html(self, text):
        if not text: return ""
        text = re.sub(r'```(\w*)\n([\s\S]*?)```', 
                     r'<pre style="background:rgba(0,0,0,0.5); padding:5px;"><code>\2</code></pre>', text)
        text = text.replace('\n', '<br>')
        text = re.sub(r'\*\*(.*?)\*\*', r'<b>\1</b>', text)
        text = re.sub(r'\*(.*?)\*', r'<i>\1</i>', text)
        return text

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