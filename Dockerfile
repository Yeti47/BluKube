FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BluKube.slnx ./
COPY src/BluKube.Contracts/BluKube.Contracts.csproj src/BluKube.Contracts/
COPY src/BluKube.Client.Core/BluKube.Client.Core.csproj src/BluKube.Client.Core/
COPY src/BluKube.Tui.Rendering/BluKube.Tui.Rendering.csproj src/BluKube.Tui.Rendering/
COPY src/BluKube.Server/BluKube.Server.csproj src/BluKube.Server/
COPY src/BluKube.Tui/BluKube.Tui.csproj src/BluKube.Tui/
COPY src/BluKube.Server.Tests/BluKube.Server.Tests.csproj src/BluKube.Server.Tests/
COPY src/BluKube.Tui.Tests/BluKube.Tui.Tests.csproj src/BluKube.Tui.Tests/
RUN dotnet restore BluKube.slnx

RUN apt-get update && apt-get install -y --no-install-recommends xvfb && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY . .
RUN dotnet publish src/BluKube.Server/BluKube.Server.csproj -c Release -o /app/publish --no-restore
# Brave is only installed in the runtime stage, so skip tests that launch it during build.
RUN dotnet test BluKube.slnx --filter "FullyQualifiedName!~BraveMediaPlayerIntegrationTests"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
       xvfb xauth dbus-x11 \
       libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libgbm1 \
       libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
       libpango-1.0-0 libcairo2 libasound2t64 libpulse0 \
       pulseaudio pulseaudio-utils \
    && curl -fsSLo /usr/share/keyrings/brave-browser-archive-keyring.gpg \
       https://brave-browser-apt-release.s3.brave.com/brave-browser-archive-keyring.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/brave-browser-archive-keyring.gpg] https://brave-browser-apt-release.s3.brave.com/ stable main" \
       > /etc/apt/sources.list.d/brave-browser-release.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends brave-browser \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

ENV BRAVE_EXECUTABLE_PATH=/usr/bin/brave-browser

RUN useradd --create-home --shell /bin/bash --uid 1000 blukube \
    && mkdir -p /run/user/1000 \
    && chown blukube:blukube /run/user/1000 \
    && chmod 700 /run/user/1000

COPY --from=build /app/publish ./
COPY docker/entrypoint.sh /app/entrypoint.sh
RUN mkdir -p /var/lib/blukube \
    && chown -R blukube:blukube /app /var/lib/blukube \
    && chmod +x /app/entrypoint.sh \
    && chmod -R a+rX /app/.playwright \
    && chmod a+rx /app/.playwright/node/linux-x64/node

USER blukube
ENV XDG_RUNTIME_DIR=/run/user/1000
VOLUME /var/lib/blukube

ENV BLUKUBE_BIND=0.0.0.0:8765
EXPOSE 8765

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -sf http://127.0.0.1:8765/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
