<h1 style="margin:0 0 12px 0;font-size:22px;line-height:1.3;font-weight:bold;color:#23262b;">Te invitaron a la consola de ZIA</h1>
<p style="margin:0 0 16px 0;">
  {{~ if model.quien_invita ~}}<strong>{{ model.quien_invita | html.escape }}</strong> te invitó{{~ else ~}}Te invitaron{{~ end }} a unirte a la consola de ZIA con el rol de <strong>{{ model.rol | html.escape }}</strong>{{~ if model.organizacion ~}}, para <strong>{{ model.organizacion | html.escape }}</strong>{{~ end ~}}.
</p>
<p style="margin:0 0 24px 0;">Para activar tu cuenta, elige tu usuario y tu contraseña desde este botón. Al hacerlo confirmas también que este correo es tuyo.</p>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px 0;">
  <tr>
    <td bgcolor="#1a7f7f" style="background-color:#1a7f7f;border-radius:8px;">
      <a href="{{ model.enlace }}" target="_blank" style="display:inline-block;padding:13px 26px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;border-radius:8px;">Crear mi cuenta</a>
    </td>
  </tr>
</table>
<p style="margin:0 0 8px 0;font-size:13px;color:#666d75;">El enlace vale <strong>{{ model.dias }} días</strong> y solo funciona una vez. Si el botón no abre, copia esta dirección en tu navegador:</p>
<p style="margin:0 0 24px 0;font-size:12px;line-height:1.5;word-break:break-all;"><a href="{{ model.enlace }}" target="_blank" style="color:#1a7f7f;">{{ model.enlace }}</a></p>
<p style="margin:0;padding-top:16px;border-top:1px solid #e3e5e8;font-size:13px;color:#666d75;">¿No esperabas esta invitación? Ignora este correo: sin abrir el enlace no se crea ninguna cuenta.</p>
