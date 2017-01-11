
# HullPixelbotCodeEditor

HullPixelbotCode is the language used to express the low level robot behaviours. It has been designed to run inside the Arduino that controls the drive motors and sensors. 

Programs are comprised of simple statements which are identified by a two character prefix. A full specification of the langauge is supplied in the repository. The program is stored in EEPROM in the Arduino and can be updated via the serial connection at any time. 

The Arduino client code is also in this project, the code here simply provided the means by which code is edited. 

The present version uses the serial port to load the program statements into the target device. A later version will also provide MQTT support for program distribution. 

This is a low level language which is not intended to be written directly, it will eventually be produced by compilation of a higher level language. 
