"""Upload as main.py or use Run on device with MicroPython."""
from machine import Pin
import time

led = Pin("LED", Pin.OUT)
for _ in range(10):
    led.toggle()
    time.sleep_ms(250)
