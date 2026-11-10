# Subir a la plataforma lo que bajó el descargador del SAT

El descargador (`motores-fiscales/sat`, `robot_sat.py`) deja las facturas de un cliente en
`<carpeta>\XML`. `subir_facturas.py` las manda a la bóveda de ese cliente en la plataforma, que
las acomoda en el mes de la fecha de cada XML. Después, el cierre del mes de ese cliente ya tiene
sus facturas para conciliar.

## Una vez

1. En la plataforma, en «Llaves API», crea una llave para la computadora del descargador
   (por ejemplo «Descargador del SAT – oficina»). Copia la clave: solo se ve al crearla.
   La llave actúa como la persona que la creó, así que solo sube a los clientes que esa persona ve.
2. En esa computadora, guarda la clave como variable de entorno del usuario
   (no en un archivo ni en el repositorio):

   ```
   setx AUXILIAR_API_KEY "la-clave"
   ```

## Cada vez que se descargue un cliente

```
python subir_facturas.py --rfc GODE561231GR8 --carpeta "D:\descargas\Panadería\robot recibidas"
python subir_facturas.py --rfc GODE561231GR8 --carpeta "D:\descargas\Panadería\robot emitidas"
```

- `--rfc` es el del cliente, tal como está en su perfil fiscal en la plataforma.
- `--prueba` solo lista lo que subiría.
- Lo ya subido se anota en `<carpeta>\subidas-a-la-plataforma.txt` y no se vuelve a mandar.
  Subir dos veces la misma factura tampoco la duplica en la plataforma.
- Si el mes de esas facturas ya está cerrado en la plataforma, las rechaza: hay que reabrirlo.

Solo usa la biblioteca estándar de Python (3.8 o más nueva). La API por omisión es
`https://api-contable.ziacentro.mx`. Para otra, usa `--api` o la variable `AUXILIAR_API`; por HTTP
sin cifrar solo acepta `localhost`.
