<!DOCTYPE html>
<html lang="es" xmlns="http://www.w3.org/1999/xhtml">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<meta name="color-scheme" content="light" />
<meta name="supported-color-schemes" content="light" />
<title>ZIA</title>
</head>
<body style="margin:0;padding:0;background-color:#f1f2f4;-webkit-text-size-adjust:100%;">
{{~ if preencabezado ~}}
<div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">{{ preencabezado | html.escape }}&#8203;&nbsp;&#8203;&nbsp;&#8203;&nbsp;&#8203;&nbsp;&#8203;&nbsp;&#8203;&nbsp;</div>
{{~ end ~}}
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f1f2f4" style="background-color:#f1f2f4;">
  <tr>
    <td align="center" style="padding:32px 16px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
        <tr>
          <td align="left" style="padding:0 4px 20px 4px;">
            {{~ if logo_url ~}}
            <img src="{{ logo_url }}" width="65" height="40" alt="ZIA" style="display:block;border:0;outline:none;text-decoration:none;height:40px;width:65px;" />
            {{~ else ~}}
            <span style="font-family:Arial,Helvetica,sans-serif;font-size:22px;font-weight:bold;color:#23262b;">ZIA</span>
            {{~ end ~}}
          </td>
        </tr>
        <tr>
          <td bgcolor="#ffffff" style="background-color:#ffffff;border:1px solid #e3e5e8;border-radius:12px;border-top:4px solid #1a7f7f;padding:32px 32px 28px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:#23262b;">
            {{ content }}
          </td>
        </tr>
        <tr>
          <td align="left" style="padding:20px 4px 0 4px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#666d75;">
            Este correo lo envió la consola de ZIA de forma automática; no respondas a este mensaje.
            {{~ if app_url ~}}
            <br />Entrar a la consola: <a href="{{ app_url }}" style="color:#666d75;text-decoration:underline;">{{ app_url_texto }}</a>
            {{~ end ~}}
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
