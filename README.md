# oci-distribution-dotnet

A .NET implementation of the [OCI Distribution Specification](https://github.com/opencontainers/distribution-spec). This is a container registry that speaks the same HTTP API as Docker Hub, ghcr.io, and other OCI-compliant registries.

It stores blobs and manifests on the local filesystem, supports chunked uploads, cross-repository blob mounting, the referrers API, and tag listing with pagination. There's no authentication -- it's meant for local development and testing, not production.

## Requirements

- .NET 10 SDK or later

## Running it

```sh
cd src/OciDistributionRegistry
dotnet run
```

The registry listens on `http://localhost:5106` by default (see `Properties/launchSettings.json`). Storage defaults to `/tmp/oci-registry` but you can change it in `appsettings.json` under `Storage:Path`.

Quick smoke test:

```sh
curl http://localhost:5106/v2/
```

You should get a `200 OK`.

## Using it with container tools

Since the registry serves plain HTTP, most clients need a flag to skip TLS.

### crane

```sh
crane copy docker.io/library/alpine:latest localhost:5106/library/alpine:latest --insecure
```

### podman

```sh
podman pull --tls-verify=false localhost:5106/library/alpine:latest
```

### docker

Docker doesn't have a per-command insecure flag. Add the registry to `/etc/docker/daemon.json`:

```json
{
  "insecure-registries": ["localhost:5106"]
}
```

Then restart and pull:

```sh
sudo systemctl restart docker
docker pull localhost:5106/library/alpine:latest
```

## Docker

```sh
docker build -t oci-registry .
docker run -p 5000:5000 -v registry-data:/data oci-registry
```

## Running the tests

```sh
dotnet test
```

The test suite is a port of the official [OCI conformance tests](https://github.com/opencontainers/distribution-spec/tree/main/conformance) to xUnit. 58 tests cover all four spec workflow categories:

- **Pull** -- blob and manifest retrieval by tag and digest, HEAD requests, Docker-Content-Digest headers
- **Push** -- streamed, monolithic, and chunked blob uploads, cross-repo mounting, manifest storage
- **Content discovery** -- tag listing with pagination and sort order, referrers API with artifact type filtering
- **Content management** -- deletion of manifests (by tag and digest) and blobs

Tests run in-process using `WebApplicationFactory`, so there's no server to start.

## API endpoints

All endpoints live under `/v2/` per the spec:

| Method | Path | What it does |
| -------- | ------ | ------------- |
| GET | `/v2/` | Version check |
| GET/HEAD | `/v2/<name>/blobs/<digest>` | Pull a blob |
| POST | `/v2/<name>/blobs/uploads/` | Start a blob upload |
| PATCH | `/v2/<name>/blobs/uploads/<id>` | Upload a chunk |
| PUT | `/v2/<name>/blobs/uploads/<id>?digest=<d>` | Finish a blob upload |
| GET | `/v2/<name>/blobs/uploads/<id>` | Check upload status |
| DELETE | `/v2/<name>/blobs/uploads/<id>` | Cancel an upload |
| DELETE | `/v2/<name>/blobs/<digest>` | Delete a blob |
| GET/HEAD | `/v2/<name>/manifests/<ref>` | Pull a manifest |
| PUT | `/v2/<name>/manifests/<ref>` | Push a manifest |
| DELETE | `/v2/<name>/manifests/<ref>` | Delete a manifest |
| GET | `/v2/<name>/tags/list` | List tags |
| GET | `/v2/<name>/referrers/<digest>` | List referrers |

## Project layout

```plain
src/OciDistributionRegistry/     The API
  Controllers/                   HTTP endpoints
  Repositories/                  Filesystem blob and manifest storage
  Services/                      Validation (digests, names, manifests)
  Middleware/                     Error handling, Docker compatibility headers
  Models/                        OCI types (descriptors, manifests, indexes)
tests/OciDistributionRegistry.ConformanceTests/
                                 OCI conformance test suite (xUnit)
```

## License

MIT
