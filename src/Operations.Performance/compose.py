"""Generate a private Compose document, independent of the real deployment."""
from policy import LABEL, run_id

ORIGIN = "https://mk816.test"


def configuration(identifier, api_image, worker_image, password, jwt, subnet, parent=None):
    run_id(identifier)
    parent = parent or identifier
    labels = {LABEL: identifier}
    database = identifier.replace("-", "_")
    connection = (f"Host=postgres;Database={database};Username=fixture;Password={password};"
                  "SSL Mode=Disable;GSS Encryption Mode=Disable;Include Error Detail=false;Maximum Pool Size=12")
    security = {"read_only": True, "tmpfs": ["/run/monkado:size=64m,mode=0700,uid=1654,gid=1654"],
                "cap_drop": ["ALL"], "security_opt": ["no-new-privileges:true"],
                "restart": "no", "labels": labels, "cgroup_parent": parent,
                "logging": {"driver": "local", "options": {"max-size": "10m", "max-file": "3"}}}
    common = {"ConnectionStrings__PostgreSql": connection, "TMPDIR": "/run/monkado",
              "DataProtection__KeysPath": "/var/lib/mon-kado/data-protection-keys",
              "GiftImages__StoragePath": "/var/lib/mon-kado/gift-images",
              "PersonalDataExports__StoragePath": "/var/lib/mon-kado/personal-data-exports"}
    volumes = ["keys:/var/lib/mon-kado/data-protection-keys", "images:/var/lib/mon-kado/gift-images",
               "exports:/var/lib/mon-kado/personal-data-exports"]
    api_environment = {**common, "ASPNETCORE_ENVIRONMENT": "Production", "ASPNETCORE_HTTP_PORTS": "8080",
                       "DOTNET_BUNDLE_EXTRACT_BASE_DIR": "/run/monkado/dotnet-bundle",
                       "Jwt__SigningKey": jwt, "AllowedHosts": "mk816.test",
                       "WebSecurity__AllowedOrigins__0": ORIGIN,
                       "WishlistSharing__FrontendOrigin": ORIGIN,
                       "GoogleAuthentication__Enabled": "false", "ReverseProxy__KnownNetworks__0": subnet,
                       "DOTNET_GCHeapHardLimit": "0x10000000", "Observability__Enabled": "true",
                       "Observability__Directory": "/run/monkado/observability", "Observability__Version": "local"}
    services = {
        "postgres": {"image": "postgres:18.6-alpine", "labels": labels, "cgroup_parent": parent,
                     "shm_size": "128m", "logging": security["logging"],
                     "environment": {"POSTGRES_USER": "fixture", "POSTGRES_PASSWORD": password, "POSTGRES_DB": database},
                     "volumes": ["postgres:/var/lib/postgresql"], "networks": ["backend"],
                     "command": ["postgres", "-c", "shared_buffers=64MB", "-c", "max_connections=40", "-c", "work_mem=2MB"],
                     "healthcheck": {"test": ["CMD", "pg_isready", "-U", "fixture", "-d", database], "interval": "2s", "retries": 30}},
        "migrations": {**security, "image": api_image, "entrypoint": ["/app/efbundle"],
                       "environment": api_environment, "networks": ["backend"], "volumes": volumes},
        "api": {**security, "image": api_image, "environment": api_environment,
                "networks": ["backend", "edge"], "volumes": volumes},
        "worker": {**security, "image": worker_image,
                   "environment": {**common, "DOTNET_ENVIRONMENT": "Local", "AuthenticationEmail__Provider": "Disabled",
                                   "AuthenticationEmail__FrontendOrigin": ORIGIN,
                                   "DOTNET_GCHeapHardLimit": "0x0C000000",
                                   "Observability__Enabled": "true", "Observability__Directory": "/run/monkado/observability",
                                   "Observability__Version": "local"},
                   "networks": ["backend"], "volumes": volumes},
        "caddy": {**security, "image": "caddy:2.11.4-alpine", "read_only": False, "cap_add": ["NET_BIND_SERVICE"],
                  "networks": {"edge": {"aliases": ["mk816.test"]}},
                  "volumes": ["caddy:/data", "caddy_config:/config"]},
    }
    return {"name": identifier, "services": services,
            "networks": {"backend": {"internal": True, "labels": labels},
                         "edge": {"internal": True, "labels": labels, "ipam": {"config": [{"subnet": subnet}]}}},
            "volumes": {name: {"labels": labels} for name in ("postgres", "keys", "images", "exports", "caddy", "caddy_config")}}
