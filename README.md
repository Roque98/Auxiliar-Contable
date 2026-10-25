# Auxiliar contable

Consola para un **despacho contable con varios clientes**: recibe sus estados de cuenta (PDF,
imágenes, fotos), sus facturas emitidas y recibidas (CFDI) y lo necesario para su perfil, y los
organiza para **conciliar las facturas con la información interna** del cliente.

**ABP Framework (tier open source)** en **.NET 10** + **Angular**. **Sin Docker**: la base de
datos y los tests usan **PostgreSQL** instalado en la máquina. Nació como copia recortada de
ZIA-Console y conserva su flujo de despliegue a VPS ([`deploy/README.md`](./deploy/README.md)).
Producción: `contable.ziacentro.mx` (web) y `api-contable.ziacentro.mx` (API).

- **Clientes del despacho** = organizaciones, con sus personas y su papel en cada una
  (Administrador, Miembro, Lector). Ver `CLAUDE.md` §1.2.6.
- **Roles**: Developer → Owner → Administrador → Usuario. Cada uno ve y gestiona solo lo
  que tiene por debajo; el Developer es invisible para los demás. Ver `CLAUDE.md` §1.1.
- **Correo, SMS y WhatsApp** por **Brevo**. Ver `CLAUDE.md` §1.2.

La solución vive en [`AuxiliarContable/`](./AuxiliarContable). La guía para agentes está en
[`CLAUDE.md`](./CLAUDE.md).

## Prerrequisitos

- **.NET SDK 10** (`dotnet --version`)
- **Node.js ≥ 24** (el Angular CLI de Angular 22 lo exige; con Node < 22.22 no arranca)
- **PostgreSQL ≥ 14** escuchando en `localhost:5432`, con un rol dedicado que tenga
  `CREATEDB` (los tests de integración crean y tiran sus propias bases)
- **ABP CLI**: `dotnet tool install -g Volo.Abp.Studio.Cli`
- (opcional) `gitleaks` para el escaneo de secretos

## Secretos (importante)

Las pass-phrases de `AuthServer:CertificatePassPhrase` y
`StringEncryption:DefaultPassPhrase` **no** están en el repo: van en
**user-secrets**. Configúralas en tu máquina (desde `AuxiliarContable/src/AuxiliarContable.HttpApi.Host`):

```bash
dotnet user-secrets set "AuthServer:CertificatePassPhrase" "<tu-frase>"
dotnet user-secrets set "StringEncryption:DefaultPassPhrase" "<tu-frase>"
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=auxiliar_contable;Username=...;Password=..."
dotnet user-secrets set "Brevo:ApiKey" "<api-key-de-brevo>"
dotnet user-secrets set "Brevo:SenderEmail" "<remitente-verificado-en-brevo>"
```

El DbMigrator lee su propia cadena de conexión (mismo comando desde
`src/AuxiliarContable.DbMigrator`) y solo carga user-secrets en Development:
`DOTNET_ENVIRONMENT=Development dotnet run`.

Nunca pongas secretos en `appsettings*.json`.

## Arranque en frío

Desde `AuxiliarContable/`:

```bash
# 1. Restaurar libs cliente (Angular/host)
abp install-libs

# 2. Crear/migrar la base en PostgreSQL + seed (usuario admin)
#    Se ejecuta DESDE la carpeta del proyecto: el host toma la configuración del
#    directorio actual, así que `dotnet run --project ...` desde la raíz no la encuentra.
cd src/AuxiliarContable.DbMigrator && DOTNET_ENVIRONMENT=Development dotnet run && cd ../..

# 3. Levantar la API  ->  https://localhost:44338/swagger
dotnet run --project src/AuxiliarContable.HttpApi.Host

# 4. Levantar el frontend Angular  ->  http://localhost:4201
cd angular && yarn start
```

Login por defecto: **admin** / **`1q2w3E*`** — es la cuenta **Developer**. Cámbiale la
contraseña al entrar y crea desde ahí al Owner (Administración → Identidad → Usuarios,
rol `owner`).

## Verificación / tests

```bash
dotnet build AuxiliarContable.slnx -c Debug                                   # warnings = errores
dotnet test  AuxiliarContable.slnx                                            # toda la suite
dotnet test  test/AuxiliarContable.Architecture.Tests/*.csproj               # arquitectura (NetArchTest)
dotnet test  test/AuxiliarContable.Integration.Tests/*.csproj                # integración (PostgreSQL + Respawn)
```

## Acceso a datos y queries

- **Escritura / modelo de dominio** → EF Core / LINQ.
- **Lectura compleja (base principal)** → `SqlQueryRaw` con SQL crudo versionado.
- **Fuentes externas de solo lectura** → Dapper vía factory.

Las **queries `.sql`** viven como recursos embebidos, versionadas, en:
`AuxiliarContable/src/AuxiliarContable.EntityFrameworkCore/**/Sql/*.sql`.

## Notas

- `refs/` (código fuente de ABP clonado como referencia) está **ignorado** en git;
  clónalo con `git clone --depth 1 https://github.com/abpframework/abp refs/abp` si lo necesitas.
- Estructura, convenciones y la "magia invisible de ABP" están documentadas en [`CLAUDE.md`](./CLAUDE.md).
