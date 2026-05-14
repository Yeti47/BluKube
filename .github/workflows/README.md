# GitHub Automation

This directory contains repository automation for publishing BluKube release artifacts.

## Publish Workflow

[publish.yml](publish.yml) runs on:

- tags matching `v*`
- manual dispatch

It publishes two Docker images to GitHub Container Registry:

- `ghcr.io/<owner>/blukube-server`
- `ghcr.io/<owner>/blukube-web`

It also publishes a self-contained Linux x64 TUI archive:

- workflow artifact: `blukube-tui-linux-x64`
- release asset on `v*` tags: `blukube-linux-x64.tar.gz`

Tagged releases also include a short usage section with:

- a brief BluKube description
- `docker run` examples for the server and web images
- basic extraction and startup instructions for the TUI binary

## Tag Releases

Create and push a version tag to publish images and attach the TUI archive to a GitHub release:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The workflow creates the GitHub release if it does not already exist.
