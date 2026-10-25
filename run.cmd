@echo off
REM Doble clic para arrancar API + Angular. Acepta los mismos parametros que run.ps1:
REM   run.cmd -Migrate    (crea/actualiza la BD la primera vez)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
