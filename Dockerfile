FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY YtCliRadio.slnx ./
COPY src/YtCliRadio/YtCliRadio.csproj src/YtCliRadio/
COPY tests/YtCliRadio.Tests/YtCliRadio.Tests.csproj tests/YtCliRadio.Tests/
RUN dotnet restore YtCliRadio.slnx

COPY . .
RUN dotnet publish src/YtCliRadio/YtCliRadio.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
       xvfb xauth dbus-x11 \
       libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libgbm1 \
       libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
       libpango-1.0-0 libcairo2 libasound2t64 libpulse0 \
       pulseaudio-utils \
    && curl -fsSLo /usr/share/keyrings/brave-browser-archive-keyring.gpg \
       https://brave-browser-apt-release.s3.brave.com/brave-browser-archive-keyring.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/brave-browser-archive-keyring.gpg] https://brave-browser-apt-release.s3.brave.com/ stable main" \
       > /etc/apt/sources.list.d/brave-browser-release.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends brave-browser \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

ENV BRAVE_EXECUTABLE_PATH=/usr/bin/brave-browser

RUN useradd --create-home --shell /bin/bash appuser

COPY --from=build /app/publish ./
RUN chmod -R a+rX /app/.playwright \
    && chmod a+rx /app/.playwright/node/linux-x64/node
USER appuser
# The app spawns its own private Xvfb instance internally (Brave runs headed
# inside it so Shields + audio stay functional). Audio is routed to the host
# PulseAudio/PipeWire socket if mounted at runtime.
ENTRYPOINT ["dotnet", "YtCliRadio.dll"]
