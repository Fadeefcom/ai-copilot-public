import sys
from PyQt6.QtWidgets import QApplication
from app import ChatWindow

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = ChatWindow(current_lang='en')
    window.show()
    sys.exit(app.exec())