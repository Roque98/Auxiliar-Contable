#!/usr/bin/env bash
# PostToolUse: tras editar código C#, compila la solución. Impide que una API de
# ABP inventada (que no compila) llegue a un commit. Solo actúa si se tocó un .cs.
input=$(cat)
echo "$input" | grep -q '\.cs' || exit 0

cd "${CLAUDE_PROJECT_DIR}/AuxiliarContable" || exit 0

if ! dotnet build AuxiliarContable.slnx -c Debug --nologo -v quiet 2>&1; then
  echo "dotnet build FALLÓ tras editar código C#. Corrige los errores antes de continuar." >&2
  echo "Si es un tipo/método de ABP, verifícalo en refs/abp (Regla Cero) — puede no existir." >&2
  exit 2
fi
exit 0
