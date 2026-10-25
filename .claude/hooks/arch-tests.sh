#!/usr/bin/env bash
# PreToolUse (Bash): antes de un `git commit`, corre los tests de arquitectura.
# Bloquea el commit si fallan. Solo actúa si el comando es un git commit.
input=$(cat)
echo "$input" | grep -q 'git commit' || exit 0

cd "${CLAUDE_PROJECT_DIR}/IgasConsole" || exit 0

# La salida del test va a stderr: este hook tiene que dejar stdout vacío (o JSON)
# para que el runner no lo tome por una respuesta inválida.
if ! dotnet test test/IgasConsole.Architecture.Tests/IgasConsole.Architecture.Tests.csproj -c Debug --nologo -v quiet >&2; then
  echo "Los tests de ARQUITECTURA fallaron. No se debe hacer commit hasta que pasen." >&2
  exit 2
fi
exit 0
