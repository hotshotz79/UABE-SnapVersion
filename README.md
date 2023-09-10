# UABE-SnapVersion
Modified UABE to streamline Snap texture editing

https://github.com/nesrak1/UABEA

-------------------------

## Main Changes

![Screenshot 2023-09-10 144718](https://github.com/hotshotz79/UABE-SnapVersion/assets/7006684/6da6b277-6245-46ae-95ae-417984e4b4e4)
* Main Window
   * Removed all buttons except INFO
   * Added Open button
   * Defaults selection to Memory
   * Automatically opens INFO window when a .bundle file is selected

![Screenshot 2023-09-10 144948](https://github.com/hotshotz79/UABE-SnapVersion/assets/7006684/67c71874-daae-43a3-8c2f-ef32b2344f21)
* Info Window
   * Only Type: Texture2D is displayed
   * Removed all buttons except Plugins (Renamed to Edit Texture), View and Edit Data
   * Edit Texture button (previously Plugins) launches Texture edit window directly
* Extra
   * Color/Highlighted OPEN and EDIT TEXTURE button for focus targetting
     
--------------------------

## Before

Originally UABE requires users to perform the following steps after starting the application:

1. Click File
2. Click Open and select .bundle file
3. Select File or Memory
4. Click Info
5. Select Texture and Click Plugins
6. Click Edit Texture
7. Click Load and select .png file
8. Click Save
9. Click Ok when Message Box appears for saving instructions
10. Close Info and Save to apply changes

## After

1. Click Open (or Ctrl+O) and select .bundle file
2. Select Texture and Click Edit Texture
3. Click Load and select .png file
4. Click Save
5. Close Info and Save to apply changes
