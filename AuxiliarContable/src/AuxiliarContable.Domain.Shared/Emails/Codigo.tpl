<h1 style="margin:0 0 12px 0;font-size:22px;line-height:1.3;font-weight:bold;color:#23262b;">{{ model.titulo | html.escape }}</h1>
<p style="margin:0 0 24px 0;">{{ model.intro | html.escape }}</p>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin:0 0 24px 0;">
  <tr>
    <td align="center" bgcolor="#f7f7f8" style="background-color:#f7f7f8;border:1px solid #e3e5e8;border-radius:10px;padding:22px 12px;">
      <div style="font-family:'Courier New',Courier,monospace;font-size:34px;line-height:1;font-weight:bold;letter-spacing:10px;color:#23262b;">{{ model.codigo | html.escape }}</div>
      <div style="margin-top:12px;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#666d75;">Vence en {{ model.minutos }} minutos · Úsalo una sola vez</div>
    </td>
  </tr>
</table>
<p style="margin:0 0 8px 0;font-size:13px;color:#666d75;">Escríbelo en la pantalla donde lo pediste. Nadie del despacho te lo va a pedir por teléfono, mensaje ni correo: no lo compartas.</p>
<p style="margin:0;padding-top:16px;border-top:1px solid #e3e5e8;font-size:13px;color:#666d75;">{{ model.aviso | html.escape }}</p>
