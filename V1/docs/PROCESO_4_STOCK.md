# Proceso 4 — Stock y extracciones

Este proceso permite operar un stock de trabajo derivado de `personas`, sin borrar ni reemplazar la base madre. Está orientado a selección por obra social, exportación CSV y recuperación de extracciones pendientes.

## Objetivo operativo

- Generar/regenerar un **stock actual** desde una `fecha_importacion` de `personas`.
- Exportar ese stock en CSV (resumen o completo).
- Ejecutar extracciones por grupos (código de obra social) con confirmación y semántica todo-o-nada.
- Recuperar una extracción pendiente por token (reintentar o cancelar).
- Reexportar extracciones completadas del stock vigente.

## Flujo del operador

1. Seleccionar fecha en el combo de importación.
2. Generar stock (primera vez) o regenerar stock (si ya existe).
3. Revisar métricas y grilla por grupos.
4. (Opcional) ajustar columnas de exportación.
5. Exportar resumen o stock completo.
6. Cargar cantidades por grupo y ejecutar extracción.
7. Si quedó pendiente, resolver con **Retry** o **Cancel**.
8. (Opcional) reexportar una extracción completada del stock actual.

## Selector de fecha (últimas 5)

- El selector muestra sólo las **últimas 5 `fecha_importacion` distintas** disponibles en `personas`.
- Si se elige una fecha vieja, la UI muestra advertencia de posible stock reducido/desactualizado.

## Generar vs regenerar (destructivo explícito)

- **Generar stock**: crea `stock_headers`/`stock_members` para la fecha seleccionada.
- **Regenerar stock**: reemplaza el stock actual y limpia estado operativo del stock anterior:
  - elimina miembros del stock previo,
  - borra metadatos de pendiente,
  - reinicia opciones de reexportación asociadas al stock anterior.

Regla importante:

- La regeneración es **destructiva sobre el stock de trabajo**.
- No conserva historial de extracciones del stock anterior.
- **No borra `personas`** (base madre intacta).

## Estado desactualizado (stale) y stock reducido

- El stock puede quedar marcado como desactualizado cuando no corresponde al último import exitoso de Sergio.
- Elegir una fecha anterior puede producir un stock con menos personas (reducido) respecto de una fecha más reciente.

## Diferencias entre CSV

### 1) Exportar resumen

- Salida agregada **sólo por código de obra social normalizado**.
- El nombre mostrado sale del catálogo JSON o, si falta, de una sugerencia determinista (el más frecuente; empate alfabético).
- Incluye totales, vendidos y disponibles.
- No contiene detalle fila a fila de personas.

### 2) Exportar stock completo

- Salida detallada del stock actual (fila por persona del stock).
- Respeta columnas seleccionadas por configuración.
- Opción “Sólo stock disponible” para excluir vendidos.

### 3) Extraer selección

- Exporta únicamente la selección solicitada por cantidades por grupo.
- Marca esos registros como vendidos al finalizar correctamente.
- Usa token de extracción para reservar/finalizar en forma consistente.

## Configuración de columnas

- Ubicación: `%LOCALAPPDATA%\PapaPersonas\config\paso4-columnas.json`.
- Si el JSON es inválido o incompleto, se aplican columnas por defecto.
- Las columnas obligatorias no se pueden desactivar.

## Catálogo de nombres de obra social

- Ubicación: `%LOCALAPPDATA%\PapaPersonas\config\paso4-obras-sociales.json`.
- Agrupa el resumen y la extracción **sólo por `codigo_obra_social` normalizado**. El nombre importado no parte grupos.
- El catálogo guarda un nombre de presentación estático por código. Afecta la grilla de Paso 4 y el CSV de resumen.
- Los CSV de detalle (stock completo, extracción y reexportación) conservan el `obra_social` original de cada fila.
- Si el JSON falta o es válido pero incompleto, el resumen siembra automáticamente sólo los códigos faltantes con una sugerencia determinista (el más frecuente; empate alfabético) y guarda esas entradas nuevas. Los nombres ya catalogados no se pisan.
- Si el JSON es inválido, se usan nombres sugeridos en memoria y no se sobrescribe el archivo.
- Si un código no tiene ningún nombre no vacío, se muestra `(Vacío)` y no se persiste un nombre inválido.
- Un catálogo completo no se reescribe al volver a abrir el resumen.
- En la grilla se puede editar el nombre y persistirlo con **Guardar nombres**.

## Extracción multi-grupo (todo o nada)

- La solicitud valida disponibilidad **por código** antes de confirmar; variantes de nombre del mismo código se suman al mismo cupo.
- Si un código no alcanza la cantidad pedida, la operación se rechaza.
- La reserva se hace por token; la finalización exige concordancia de metadatos pendientes (validación CAS).
- Resultado esperado: o se completa toda la extracción, o no se confirma ninguna venta parcial.

## Recuperación de pendiente (Retry / Cancel)

Si existe una pendiente al abrir/actualizar el proceso:

- **Retry**: reintenta exportar el token pendiente y finalizar venta.
- **Cancel**: libera reservas no vendidas y limpia metadatos de pendiente.

Nota: cancelar también intenta limpiar archivos temporales/salida de esa pendiente.

## Reexportación

- La reexportación busca tokens vendidos **sólo dentro del stock actual**.
- Si se regeneró stock, tokens de stock anterior no forman parte del contexto reexportable vigente.

## Seguridad operativa y privacidad

- `personas` (base madre) no se elimina desde Proceso 4.
- La consola de actividad evita logging de PII por fila (mensajes agregados/técnicos).
- No se guarda historial funcional de extracciones después de regenerar stock.

## Formato CSV

- Exportación **sólo CSV**.
- Codificación UTF-8 con BOM.
- Delimitador fijo coma (`,`).
- Delimitador configurable queda fuera de alcance de esta versión.
