# OCR to Markdown Converter

A desktop application built in C# using the **Avalonia UI** framework, designed to convert documents (PDFs and common images) into clean, structured **Markdown (.md)** format.

The application intelligently combines direct text extraction from PDFs with advanced **OCR (Optical Character Recognition)** using the Tesseract library. The extracted text is then cleaned, automatically detecting headings and bullet points, correcting spelling errors using the Czech Hunspell dictionary, and reconstructing hyphenated words split at the end of lines.

---

## Key Features

* **Smart PDF Reading:** If a PDF contains a text layer, the app reads it directly. If the page is scanned, it automatically triggers OCR.
* **Advanced OCR:** Powered by the **Tesseract** library (supporting both Czech and English) and **OpenCV** for image preprocessing (Otsu's thresholding, noise reduction).
* **Spellcheck Correction:** Integrated **Hunspell** dictionary to fix typos generated during the OCR process.
* **Structure Reconstruction:** Automatic detection of paragraphs, line breaks, various heading levels, bulleted lists, and stitching back together hyphenated words at the end of lines.
* **Cross-platform Deployment:** Full support for running within an isolated **Docker** environment.

---

## Requirements

To run the application via Docker (recommended method), you will need:
* **Docker Desktop** (or Docker Engine)
* **X-Server** (required on Windows/macOS to display the GUI from the container):
  * **Windows:** VcXsrv (or Xming)
  * **macOS:** XQuartz

---

## Directory Structure (Key Files)

For the application to run correctly, the project must contain the following files and folders:

```text
.
├── App.axaml
├── App.axaml.cs
├── App.csproj
├── app.manifest
├── Assets/
│   ├── cs_CZ.aff               
│   ├── cs_CZ.dic              
│   └── tessdata/             
│       ├── ces.traineddata
│       └── eng.traineddata
├── Dockerfile
├── MainWindow.axaml
├── MainWindow.axaml.cs
├── Program.cs
└── x64/                        
    ├── libleptonica-1.82.0.so
    ├── libtesseract50.so
    └── libtesseract-5.so
```

---
## Docker deployment guide
1. Build the Docker Image
Run the following command in the root directory of the project:

`docker build -t ocr-markdown-app .`

2. Run the Application
## Linux

Allow display access for Docker and run the container:
```markdown
```bash
xhost +local:docker 
docker run -it --rm \
  -e DISPLAY=$DISPLAY \
  -v /tmp/.X11-unix:/tmp/.X11-unix \
  ocr-markdown-app
```

## Windows
Launch VcXsrv (choose Multiple windows, set Display number to 0, and check Disable access control).

Find your local IP address (e.g., 192.168.1.50).

Run the container (replace the IP address below with your own):

`docker run -it --rm -e DISPLAY=192.168.1.50:0.0 ocr-markdown-app`

## macOS

Launch XQuartz (go to Preferences -> Security and check Allow connections from clients).

In your terminal, authorize the connection:

   ` xhost + 127.0.0.1 `

Spusťte kontejner:

```markdown
```bash
 docker run -it --rm -e DISPLAY=docker.for.mac.host.internal:0 \
 ocr-markdown-app 
 ```


## Local Development (Without Docker)

Install the .NET 8.0 SDK.

Ensure you have the native Tesseract and Leptonica libraries installed on your system (on Linux, you can install them via sudo apt install libleptonica-dev libtesseract-dev).

Restore dependencies and run the project:

`dotnet restore`
`dotnet run`