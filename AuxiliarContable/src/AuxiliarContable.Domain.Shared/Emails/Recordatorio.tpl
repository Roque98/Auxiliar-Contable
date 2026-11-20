<h1 style="margin:0 0 12px 0;font-size:22px;line-height:1.3;font-weight:bold;color:#23262b;">Te falta subir {{ model.total }} documento{{ if model.total != 1 }}s{{ end }}</h1>
<p style="margin:0 0 16px 0;">Hola{{~ if model.nombre ~}} {{ model.nombre | html.escape }}{{~ end ~}}. Para <strong>{{ model.organizacion | html.escape }}</strong> dijiste que subirías esto después:</p>
<ul style="margin:0 0 16px 0;padding-left:20px;">
{{~ for p in model.pendientes ~}}
  <li style="margin:0 0 6px 0;">{{ p.que | html.escape }} de <strong>{{ p.operacion | html.escape }}</strong> ({{ p.fecha }}{{~ if p.importe ~}}, {{ p.importe }}{{~ end ~}}) <span style="color:#666d75;">· hace {{ p.dias }} días</span></li>
{{~ end ~}}
</ul>
{{~ if model.total > model.pendientes.size ~}}
<p style="margin:0 0 16px 0;font-size:13px;color:#666d75;">…y {{ model.total - model.pendientes.size }} más.</p>
{{~ end ~}}
<p style="margin:0 0 24px 0;">Mientras falten, la contabilidad y el cálculo de impuestos del mes quedan incompletos.</p>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px 0;">
  <tr>
    <td bgcolor="#2c5282" style="background-color:#2c5282;border-radius:8px;">
      <a href="{{ model.enlace }}" target="_blank" style="display:inline-block;padding:13px 26px;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;border-radius:8px;">Subir lo que falta</a>
    </td>
  </tr>
</table>
<p style="margin:0;padding-top:16px;border-top:1px solid #e3e5e8;font-size:13px;color:#666d75;">Si ya no lo vas a subir, entra y elige «No lo subiré»: así dejamos de recordártelo. Te escribimos como máximo una vez por semana.</p>
