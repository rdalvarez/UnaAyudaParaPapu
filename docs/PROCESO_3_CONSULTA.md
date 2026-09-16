# Proceso 3 — Consulta y exportación guiada (V1)

Proceso 3 V1 está diseñado para extracción simple y segura desde `personas`.

## Quick path

1. Abrí pestaña **3. Consultar datos**.
2. Definí filtros guiados (CUIL exacto, filtros AND por columna, rango de `fecha_importacion`).
3. Elegí columnas de salida.
4. Ejecutá preview paginado.
5. Exportá CSV con opción de cancelación.

## Alcance V1

| Incluye | No incluye |
|---|---|
| CUIL exacto | Editor SQL libre |
| Filtros AND guiados (Igual a, Contiene, Comienza con, Mayor o igual que, Menor o igual que, Entre) | Builder avanzado tipo BI |
| Rango de `fecha_importacion` | Joins/tablas externas |
| Selección de columnas | Transformaciones complejas |
| Preview paginado + KPI (`total`, `page rows`, `latest fecha_importacion`) | Cargas masivas en memoria |
| Export CSV streaming con cancelar/progreso | Export con lógica custom por fila |

Los filtros guiados usan sólo valores ya importados. `edad` filtra `personas.edad`; no se calcula desde `fecha_nacimiento`. El combo muestra etiquetas en español (DNI, Sexo, Código de obra social, Partido, Nacionalidad, CUIT del empleador, Fecha de nacimiento, etc.); la consulta y la exportación siguen usando los códigos internos (`dni`, `eq`, `contains`, `starts_with`, `gte`, `lte`, `between`).

## Regla de privacidad y logging

- La grilla de preview puede mostrar datos de filas porque es la vista operativa.
- Consola/logs deben quedar en modo agregado: conteos, estado, progreso y fechas.
- No registrar CUIL, apellido/nombre u otros valores de fila en mensajes de actividad.

## Fallback aprobado para análisis complejos

Cuando el caso excede V1 (consultas complejas, cruces avanzados, análisis exploratorio), el fallback aprobado es **DBeaver** sobre la base local de DuckDB, bajo acceso de solo lectura y controlado por el equipo.
