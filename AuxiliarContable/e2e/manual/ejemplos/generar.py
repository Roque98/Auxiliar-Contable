# -*- coding: utf-8 -*-
"""Genera los archivos de EJEMPLO del manual (todo inventado; los RFC tienen dígito verificador válido): el estado de cuenta de noviembre de «Panadería La
Espiga», sus facturas XML y su Constancia de Situación Fiscal. Imitan el texto de los documentos reales para que el
motor fiscal los lea igual, pero ningún dato es de un cliente.

    python manual/ejemplos/generar.py      (necesita PyMuPDF: pip install pymupdf)

El mes es noviembre de 2026 porque la bóveda agrupa por la fecha de subida (ver docs/manuales).
"""
import os

import fitz

AQUI = os.path.dirname(os.path.abspath(__file__))
RFC = "GODE561231GR8"
CLIENTES = {"CPO150101AB4": "CAFETERIA EL PORTAL SA DE CV", "HLA120101AB8": "HOTEL LOS ARCOS SA DE CV",
            "RSO140101AB7": "RESTAURANTE SOL DE ORO SA DE CV"}
PROVEEDOR = ("HCE100101AA8", "HARINERA DEL CENTRO SA DE CV")

# (fecha, naturaleza, importe, descripción, renglones de detalle)
MOVIMIENTOS = [
    ("03/11/2026", "abono", 11600.00, "SPEI RECIBIDO", ["DEL CLIENTE CAFETERIA EL PORTAL SA DE CV", "RFC CPO150101AB4"]),
    ("05/11/2026", "cargo", 4640.00, "SPEI ENVIADO", ["AL PROVEEDOR HARINERA DEL CENTRO SA DE CV", "RFC HCE100101AA8"]),
    ("06/11/2026", "cargo", 18.00, "COMISION SPEI", []),
    ("06/11/2026", "cargo", 2.88, "IVA COMISION", []),
    ("10/11/2026", "abono", 3480.00, "DEPOSITO EN EFECTIVO", []),
    ("12/11/2026", "abono", 5800.00, "SPEI RECIBIDO", ["DEL CLIENTE HOTEL LOS ARCOS SA DE CV", "RFC HLA120101AB8"]),
    ("16/11/2026", "cargo", 1250.00, "PAGO DE SERVICIO LUZ", []),
]
SALDO_INICIAL = 25000.00


def estado_de_cuenta():
    doc = fitz.open()
    abonos = sum(m[2] for m in MOVIMIENTOS if m[1] == "abono")
    cargos = sum(m[2] for m in MOVIMIENTOS if m[1] == "cargo")
    pg = doc.new_page()
    y = 50
    for t in ("BANORTE  Banco Mercantil del Norte", "ESTADO DE CUENTA  CUENTA DE CHEQUES", "GUADALUPE HERRERA EJEMPLO",
              "R.F.C. %s" % RFC, "PERIODO DEL 01/11/2026 AL 30/11/2026", "No. DE CUENTA 0198765432   CLABE 072180001987654329",
              "SALDO ANTERIOR %s" % format(SALDO_INICIAL, ",.2f"), "DEPOSITOS %s" % format(abonos, ",.2f"),
              "RETIROS %s" % format(cargos, ",.2f"), "SALDO FINAL %s" % format(SALDO_INICIAL + abonos - cargos, ",.2f")):
        pg.insert_text((40, y), t, fontsize=9)
        y += 14
    pg = doc.new_page()
    for x, t in ((40, "FECHA"), (110, "DESCRIPCION"), (380, "DEPOSITOS"), (455, "RETIROS"), (530, "SALDO")):
        pg.insert_text((x, 60), t, fontsize=8)
    y, saldo = 80, SALDO_INICIAL
    for fecha, nat, imp, desc, detalle in MOVIMIENTOS:
        saldo += imp if nat == "abono" else -imp
        txt = format(imp, ",.2f")
        pg.insert_text((40, y), fecha, fontsize=8)
        pg.insert_text((110, y), desc, fontsize=8)
        x = 380 if nat == "abono" else 455
        pg.insert_text((x + 40 - fitz.get_text_length(txt, fontsize=8), y), txt, fontsize=8)
        st = format(saldo, ",.2f")
        pg.insert_text((560 - fitz.get_text_length(st, fontsize=8), y), st, fontsize=8)
        y += 12
        for d in detalle:
            pg.insert_text((110, y), d, fontsize=8)
            y += 12
    doc.save(os.path.join(AQUI, "banorte-noviembre.pdf"))


def cfdi(nombre, folio, emisor, receptor, nombre_emisor, nombre_receptor, fecha, total, uuid):
    sub = round(total / 1.16, 2)
    iva = round(total - sub, 2)
    x = ('<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" '
         'Serie="A" Folio="%s" Fecha="%sT10:00:00" Moneda="MXN" SubTotal="%.2f" Total="%.2f" TipoDeComprobante="I" '
         'MetodoPago="PUE" FormaPago="03" Exportacion="01" LugarExpedicion="06600"><cfdi:Emisor Rfc="%s" Nombre="%s" '
         'RegimenFiscal="612"/><cfdi:Receptor Rfc="%s" Nombre="%s" DomicilioFiscalReceptor="06600" RegimenFiscalReceptor="601" '
         'UsoCFDI="G03"/><cfdi:Conceptos><cfdi:Concepto ClaveProdServ="50181900" Cantidad="1" ClaveUnidad="H87" '
         'Descripcion="Pan y reposteria" ValorUnitario="%.2f" Importe="%.2f" ObjetoImp="02"/></cfdi:Conceptos>'
         '<cfdi:Impuestos TotalImpuestosTrasladados="%.2f"><cfdi:Traslados><cfdi:Traslado Base="%.2f" Impuesto="002" '
         'TipoFactor="Tasa" TasaOCuota="0.160000" Importe="%.2f"/></cfdi:Traslados></cfdi:Impuestos><cfdi:Complemento>'
         '<tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="%s" RfcProvCertif="AAA010101AAA"/>'
         '</cfdi:Complemento></cfdi:Comprobante>' % (folio, fecha, sub, total, emisor, nombre_emisor, receptor, nombre_receptor,
                                                      sub, sub, iva, sub, iva, uuid))
    with open(os.path.join(AQUI, nombre), "w", encoding="utf-8") as f:
        f.write(x)


def facturas():
    yo = "GUADALUPE HERRERA EJEMPLO"
    cfdi("factura-A101-cafeteria.xml", "101", RFC, "CPO150101AB4", yo, CLIENTES["CPO150101AB4"], "2026-11-03", 11600.00,
         "11111111-0000-4000-8000-000000000101")
    cfdi("factura-A102-hotel.xml", "102", RFC, "HLA120101AB8", yo, CLIENTES["HLA120101AB8"], "2026-11-11", 5800.00,
         "11111111-0000-4000-8000-000000000102")
    cfdi("factura-A103-restaurante.xml", "103", RFC, "RSO140101AB7", yo, CLIENTES["RSO140101AB7"], "2026-11-14", 2320.00,
         "11111111-0000-4000-8000-000000000103")
    cfdi("compra-harinera.xml", "8841", PROVEEDOR[0], RFC, PROVEEDOR[1], yo, "2026-11-04", 4640.00,
         "22222222-0000-4000-8000-000000008841")


def constancia():
    renglones = [
        "CÉDULA DE IDENTIFICACIÓN FISCAL", "CONSTANCIA DE SITUACIÓN FISCAL",
        "RFC:", RFC, "CURP:", "GODE561231MDFRRR09",
        "Nombre (s):", "GUADALUPE", "Primer Apellido:", "HERRERA", "Segundo Apellido:", "EJEMPLO",
        "Fecha inicio de operaciones:", "01 DE ENERO DE 2020", "Estatus en el padrón:", "ACTIVO",
        "Datos del domicilio registrado", "Código Postal:", "06600", "Nombre de la Entidad Federativa:", "CIUDAD DE MÉXICO",
        "Actividades Económicas:", "Orden", "Actividad Económica", "Porcentaje", "Fecha Inicio", "Fecha Fin",
        "1", "Panificación tradicional", "100", "01/01/2020",
        "Regímenes:", "Régimen", "Fecha Inicio", "Fecha Fin",
        "Régimen de las Personas Físicas con Actividades Empresariales y Profesionales", "01/01/2020",
        "Obligaciones:",
        "Pago definitivo mensual de IVA.", "01/01/2020",
        "Pago provisional mensual de ISR por actividades empresariales.", "01/01/2020",
        "Declaración anual de ISR. Personas Físicas.", "01/01/2020",
    ]
    doc = fitz.open()
    pg = doc.new_page()
    y = 40
    for t in renglones:
        pg.insert_text((40, y), t, fontsize=9, fontname="helv")
        y += 13
    doc.save(os.path.join(AQUI, "constancia-panaderia.pdf"))


if __name__ == "__main__":
    estado_de_cuenta()
    facturas()
    constancia()
    print("listo:", sorted(f for f in os.listdir(AQUI) if not f.endswith(".py")))
