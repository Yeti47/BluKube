BluKube is a self-hosted YouTube audio player with a containerized server, a browser client, and a native terminal client. Playback runs through Brave with Shields enabled for ad-blocking.

## Artifacts

- Server image: `ghcr.io/{{OWNER}}/blukube-server:{{TAG}}`
- Web image: `ghcr.io/{{OWNER}}/blukube-web:{{TAG}}`
- TUI archive: `blukube-linux-x64.tar.gz`

## Run the server

```bash
docker run --rm \
  -p 8765:8765 \
  -e BLUKUBE_BIND=0.0.0.0:8765 \
  -e BLUKUBE_TOKEN=replace-me \
  ghcr.io/{{OWNER}}/blukube-server:{{TAG}}
```

## Run the web client

```bash
docker run --rm -p 8080:8080 ghcr.io/{{OWNER}}/blukube-web:{{TAG}}
```

Open `http://127.0.0.1:8080` and enter the BluKube server URL and auth token.

## Use the TUI binary

1. Download `blukube-linux-x64.tar.gz` from this release.
2. Extract it with `tar -xzf blukube-linux-x64.tar.gz`.
3. Start the client with `./blukube`.
4. When prompted, enter the BluKube server URL and auth token.
