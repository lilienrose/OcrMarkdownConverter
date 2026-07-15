FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["App.csproj", "./"]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y \
    libgdiplus \
    libx11-6 \
    libice6 \
    libsm6 \
    libext6 \
    librender1 \
    libleptonica-dev \
    libtesseract-dev \
    tesseract-ocr \
    tesseract-ocr-ces \
    tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/x64

RUN LEPT_PATH=$(find /usr/lib -name "liblept.so*" | head -n 1) && \
    TESS_PATH=$(find /usr/lib -name "libtesseract.so*" | head -n 1) && \
    ln -sf $LEPT_PATH /app/libleptonica-1.82.0.so && \
    ln -sf $LEPT_PATH /app/x64/libleptonica-1.82.0.so && \
    ln -sf $TESS_PATH /app/libtesseract50.so && \
    ln -sf $TESS_PATH /app/x64/libtesseract50.so

RUN ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 /app/libdl.so && \
    ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 /app/x64/libdl.so

ENTRYPOINT ["dotnet", "App.dll"]