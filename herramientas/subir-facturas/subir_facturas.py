"""Sube a la plataforma las facturas (XML) que bajó el descargador del SAT.

El descargador (motores-fiscales/sat/robot_sat.py) deja los XML en <carpeta>/XML. Este script
los manda a la bóveda del cliente que tiene ese RFC en su perfil fiscal:

    set AUXILIAR_API_KEY=...        (la clave de «Llaves API»; nunca en un archivo del repo)
    python subir_facturas.py --rfc GODE561231GR8 --carpeta "D:\\descargas\\cliente\\robot recibidas"

- Cada factura cae sola en el mes de la fecha de su XML (lo decide la plataforma).
- Subir dos veces la misma no la duplica; aun así, lo ya subido se anota en
  <carpeta>/subidas-a-la-plataforma.txt y no se vuelve a mandar.
- Solo usa la biblioteca estándar de Python.
"""
import argparse
import hashlib
import json
import os
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

API = "https://api-contable.ziacentro.mx"
REGISTRO = "subidas-a-la-plataforma.txt"


def _multipart(nombre, contenido):
    frontera = uuid.uuid4().hex
    cuerpo = b"".join([
        f"--{frontera}\r\n".encode(),
        f'Content-Disposition: form-data; name="archivo"; filename="{nombre}"\r\n'.encode(),
        b"Content-Type: application/xml\r\n\r\n",
        contenido,
        f"\r\n--{frontera}--\r\n".encode(),
    ])
    return cuerpo, f"multipart/form-data; boundary={frontera}"


def subir(api, clave, rfc, ruta):
    with open(ruta, "rb") as f:
        contenido = f.read()
    cuerpo, tipo = _multipart(os.path.basename(ruta), contenido)
    url = f"{api}/api/app/boveda/subir-factura?" + urllib.parse.urlencode({"rfc": rfc})
    peticion = urllib.request.Request(url, data=cuerpo, method="POST",
                                      headers={"Content-Type": tipo, "X-Api-Key": clave, "Accept": "application/json"})
    # La consola local usa un certificado de desarrollo: solo ahí no se verifica.
    local = urllib.parse.urlparse(api).hostname in ("localhost", "127.0.0.1")
    contexto = ssl._create_unverified_context() if local else None  # noqa: S323
    try:
        with urllib.request.urlopen(peticion, timeout=120, context=contexto) as r:
            return True, json.load(r)
    except urllib.error.HTTPError as e:
        try:
            mensaje = json.load(e).get("error", {}).get("message") or str(e)
        except ValueError:
            mensaje = str(e)
        return False, mensaje
    except urllib.error.URLError as e:
        return False, f"no se pudo conectar con la plataforma ({e.reason})"


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--rfc", required=True, help="RFC del cliente (el de su perfil fiscal en la plataforma)")
    p.add_argument("--carpeta", required=True, help="carpeta del descargador (la que tiene XML/)")
    p.add_argument("--api", default=os.environ.get("AUXILIAR_API", API), help=f"URL de la API (por omisión {API})")
    p.add_argument("--prueba", action="store_true", help="solo dice qué subiría")
    a = p.parse_args()

    api = a.api.rstrip("/")
    destino = urllib.parse.urlparse(api)
    # La clave viaja en una cabecera: por HTTP sin cifrar solo a la propia máquina.
    if destino.scheme != "https" and destino.hostname not in ("localhost", "127.0.0.1"):
        sys.exit("La API tiene que ser https (la clave viaja en cada petición).")
    clave = os.environ.get("AUXILIAR_API_KEY", "").strip()
    if not clave and not a.prueba:
        sys.exit("Falta la variable AUXILIAR_API_KEY con la clave de «Llaves API».")

    carpeta_xml = os.path.join(a.carpeta, "XML")
    if not os.path.isdir(carpeta_xml):
        carpeta_xml = a.carpeta
    archivos = sorted(os.path.join(raiz, n) for raiz, _, nombres in os.walk(carpeta_xml)
                      for n in nombres if n.lower().endswith(".xml"))

    registro = os.path.join(a.carpeta, REGISTRO)
    ya = set()
    if os.path.exists(registro):
        with open(registro, encoding="utf-8") as f:
            ya = {linea.split("\t")[0] for linea in f if linea.strip()}

    cuenta = {"subidas": 0, "ya estaban": 0, "con error": 0}
    for ruta in archivos:
        with open(ruta, "rb") as f:
            huella = hashlib.sha256(f.read()).hexdigest()
        if huella in ya:
            cuenta["ya estaban"] += 1
            continue
        if a.prueba:
            print("subiría", os.path.basename(ruta))
            cuenta["subidas"] += 1
            continue
        ok, r = subir(api, clave, a.rfc, ruta)
        if not ok:
            cuenta["con error"] += 1
            print("NO", os.path.basename(ruta), "-", r)
            # Un RFC que no es de un cliente o una plataforma caída fallan igual para todas: no tiene caso seguir.
            if "RFC" in str(r) or "conectar" in str(r):
                break
            continue
        cuenta["ya estaban" if r.get("esDuplicado") else "subidas"] += 1
        with open(registro, "a", encoding="utf-8") as f:
            f.write(f"{huella}\t{os.path.basename(ruta)}\n")
        print("ya estaba" if r.get("esDuplicado") else "subida", os.path.basename(ruta), r.get("aviso") or "")

    pendientes = len(archivos) - sum(cuenta.values())
    resumen = ", ".join(f"{v} {k}" for k, v in cuenta.items())
    print(f"\n{len(archivos)} XML: {resumen}" + (f", {pendientes} sin intentar" if pendientes else "") + ".")
    sys.exit(1 if cuenta["con error"] else 0)


if __name__ == "__main__":
    main()
