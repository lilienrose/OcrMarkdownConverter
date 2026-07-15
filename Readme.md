# OCR do Markdown Převodník

Desktopová aplikace vytvořená v C# pomocí frameworku Avalonia UI, která slouží k převodu dokumentů (PDF a běžných obrázků) do čistého formátu Markdown (.md).

Aplikace inteligentně kombinuje přímé čtení textu z PDF s pokročilým OCR (optickým rozpoznáváním znaků) pomocí knihovny Tesseract. Extrahovaný text následně čistí, automaticky detekuje nadpisy i odrážky, opravuje překlepy pomocí českého slovníku Hunspell a rekonstruuje slova rozdělená spojovníkem na konci řádku.

---

## Klíčové vlastnosti

* **Chytré čtení PDF:** Pokud PDF obsahuje textovou vrstvu, aplikace ji přečte přímo. Pokud je stránka naskenovaná, automaticky spustí OCR.
* **Pokročilé OCR:** Využívá knihovnu Tesseract (s podporou češtiny a angličtiny) a OpenCV pro předzpracování obrazu (Otsuovo prahování, odstranění šumu).
* **Korekce pravopisu (Spellcheck):** Integrace Hunspell slovníku pro opravu chyb vzniklých při OCR.
* **Rekonstrukce struktury:** Automatická detekce odstavců, konců řádků, nadpisů různých úrovní, odrážkových seznamů a spojování rozdělených slov na konci řádku.
* **Multiplatformní nasazení:** Plná podpora běhu v izolovaném prostředí Dockeru.

---

## Požadavky pro spuštění

Pro spuštění aplikace v Dockeru (doporučená cesta) potřebujete:
* **Docker Desktop** (nebo Docker Engine)
* **X-Server** (vyžadováno pro Windows/macOS pro zobrazení grafického rozhraní z kontejneru):
  * **Windows:** VcXsrv (nebo Xming)
  * **macOS:** XQuartz

## Adresářová struktura (Důležité soubory)

Projekt musí pro správný běh obsahovat následující soubory a složky:
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

## Návod k nasazení přes Docker
1. Sestavení Docker obrazu
Spustit v kořenovém adresáři

docker build -t ocr-markdown-app .

2. Spuštění aplikace
## Linux

Povolit přístup k displeji pro Docker a spustit kontejner

xhost +local:docker
docker run -it --rm \
  -e DISPLAY=$DISPLAY \
  -v /tmp/.X11-unix:/tmp/.X11-unix \
  ocr-markdown-app

## Windows
Spusťte VcXsrv (zvolte Multiple windows, Display number: 0, a zaškrtněte Disable access control).

Zjistěte svou lokální IP adresu (např. 192.168.1.50).
Spusťte kontejner (nahraďte IP adresu vaší vlastní):

docker run -it --rm -e DISPLAY=192.168.1.50:0.0 ocr-markdown-app

## macOS

Spusťte XQuartz (v Preferences -> Security povolte Allow connections from clients).
V terminálu povolte připojení

    xhost + 127.0.0.1

    Spusťte kontejner:

docker run -it --rm -e DISPLAY=docker.for.mac.host.internal:0 ocr-markdown-app


# Lokální vývoj bez Dockeru
1. Nainstalujte si .NET 8.0 SDK.
2. Ujistěte se, že máte v systému nainstalované nativní knihovny Tesseract a Leptonica (na Linuxu např. přes apt install libleptonica-dev libtesseract-dev).
3. Pro obnovu balíčků a spuštění použijte příkazy:
dotnet restore
dotnet run