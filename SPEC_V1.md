# PapaPersonas — Especificación V1

Aplicación local para que una persona sin conocimientos de SQL pueda importar, actualizar, consultar y exportar información de personas, obras sociales, teléfonos, domicilios y datos de contacto. La V1 prioriza simpleza, operación offline, importaciones masivas por archivo, CUIL único y backups manuales.

## Resumen ejecutivo

| Área | Decisión V1 |
|---|---|
| Usuario principal | Padre del usuario, uso hogareño, una sola PC principal. |
| Plataforma | Windows 11. |
| Hardware objetivo | i7-8565U, 16 GB RAM, 273 GB libres. |
| Stack | C#/.NET 10 LTS, WPF, DuckDB.NET, DuckDB local. |
| Base | Archivo DuckDB local, no SQL Server. |
| Operación | Offline; Dropbox sólo para copias de backup. |
| Volumen esperado | Hasta 5 millones de registros. |
| Identidad | Un único registro por CUIL. |
| Actualización | Contrato por columna: ausente preserva, presente con valor reemplaza, presente vacío limpia. |
| Histórico | No se guarda histórico de cambios en V1. |
| Exportación | CSV. |
| Seguridad | Cuenta Windows + Dropbox; sin password propia ni cifrado adicional en V1. |

## Objetivos de la V1

- Automatizar el flujo completo actual de trabajo con archivos de Hernán, Sergio y clientes.
- Mantener una base principal limpia con un solo registro por CUIL.
- Permitir filtros por cualquier columna sin que el usuario escriba SQL.
- Permitir elegir qué columnas mostrar/exportar.
- Generar CSV para call centers.
- Crear backups manuales consistentes.
- Registrar acciones y errores en logs.

## Fuera de alcance V1

- Edición manual directa de registros desde pantalla.
- Histórico por campo o por registro.
- Usuarios, roles o login propio.
- Sincronización multiusuario.
- Base activa dentro de Dropbox.
- Renombrar columnas desde la aplicación.
- Resolver automáticamente duplicados por CUIL.

## Flujo funcional principal

La aplicación tiene 4 procesos principales:

| Proceso | Rol | ¿Actualiza `personas`? |
|---|---|---|
| Proceso de datos de Hernán | Limpieza/preparación básica: aísla duplicados, valida sólo el mínimo requerido, columnas extra son aviso no bloqueante, prepara columnas para el pedido a Sergio. | No. |
| Actualización de base de Sergio | Persistencia estricta: importa la devolución enriquecida, valida esquema/gobernanza, carga en staging, rechaza inválidos/duplicados y actualiza la base definitiva. | Sí. |
| Consulta de datos guardados de Sergio | Filtra los datos ya guardados y exporta CSV para call centers. | No (sólo lectura). |
| Stock y extracciones | Genera stock operativo por fecha, exporta CSV y ejecuta extracciones con recuperación de pendiente por token. | Sí (sobre tablas de stock, no sobre `personas`). |


### 1. Proceso de datos de Hernán (preparación)

Entrada esperada: archivo tipo `1 - PedidoOOSS_...xlsx`. El número exacto de columnas puede variar entre entregas; no se exige un total fijo.

Propósito de esta etapa:

- Paso preparatorio/aislado para prolijar datos y generar el pedido a Sergio.
- No actualiza directamente la base definitiva `personas`.
- Puede aceptar variaciones de columnas siempre que existan las mínimas requeridas para el pedido a Sergio.

La aplicación debe:

1. Pedir seleccionar el archivo.
2. Validar columnas mínimas de preparación para generar pedido a Sergio:
   - `CUIL`, `CUIL_APENOM`, `CUIL_CODOS`, `CUIL_DESCRIPOS`, `CUIL_FECHANAC`, `CUIL_EDAD`.
   Las columnas adicionales de Hernán no bloquean esta etapa preparatoria.
3. Cargar el archivo en staging.
4. Detectar filas sin CUIL.
5. Detectar CUIL duplicados.
6. Generar archivo de rechazados/revisión para filas sin CUIL o duplicadas.
7. Generar el archivo para Sergio con las columnas esperadas.

Implementación actual (Paso 1):

- Se implementó un pipeline programático que procesa el XLSX de Hernán y genera:
  - CSV para Sergio con columnas `CUIL`, `APELLIDO_NOMBRE`, `CD_OS`, `DESCRIPCION O_S`, `FECHA_NAC`, `EDAD`.
  - CSV de rechazados con `source_row_number`, `reason_code`, `raw_cuil`, `normalized_cuil`, `message`.
- La validación de CUIL y duplicados de lote se aplica en este proceso preparatorio.
- Este proceso no persiste en `personas`.
- Se agregó CLI estable para ejecutar Paso 1 sin UI y archivo editable `config/PARA_HERNAN.json` para definir columnas de salida hacia Sergio sin tocar código.
- Se agregó pantalla WPF de Paso 1 para operación sin CLI:
  - selección de archivo/folder/config,
  - ejecución asíncrona con bloqueo de controles y prevención de doble inicio,
  - bloqueo de cierre durante ejecución,
  - resumen agregado + rutas de salida + tiempo de ejecución,
  - botón para abrir carpeta de salida.

Implementación actual (Proceso 2A, con UI WPF conectada):

- Validación estricta de encabezados de devolución de Sergio (bloquea columnas desconocidas).
- Creación de `import_runs` (`stage_type=sergio_return`, transición `analyzing -> ready_for_confirmation`).
- Carga streaming del XLSX hacia `personas_staging`.
- Validación estructural de CUIL en carga y rechazo por fila para vacíos/malformados.
- Rechazo set-based de TODOS los duplicados por CUIL normalizado dentro del lote.
- Cálculo set-based de `rows_to_insert` vs `rows_to_update` comparando staging válido contra `personas`.
- Persistencia de `source_columns_present_json` con campos canónicos presentes para preservar semántica ausente vs presente-vacío.

Implementación actual (Proceso 2B backend + confirmación UI WPF):

- Apply transaccional desde `personas_staging` hacia `personas` sólo para `validation_outcome='valid'`.
- Guardas de estado/etapa: sólo permite `sergio_return` en `ready_for_confirmation`.
- Rechaza doble apply (run ya completado deja de estar ready).
- Contrato por columna usando `source_columns_present_json`:
  - columna ausente en JSON => preservar valor existente,
  - columna presente con null => limpiar,
  - columna presente con valor => reemplazar.
- `fecha_actualizacion` se setea/actualiza para cada fila aplicada.
- Exportación opcional de CSV de rechazados (source_row_number/reason/normalized_cuil).
- Confirmación explícita previa al apply con advertencia de backup recomendado (sin backup automático).
- El apply UI usa sólo el `import_id` actualmente analizado (ownership en estado de sesión) y luego lo limpia al completar.
- La UI muestra únicamente resúmenes agregados; no expone filas/valores PII.

### 2. Generar archivo para Sergio

Salida esperada: CSV o Excel según necesidad operativa posterior; por el flujo actual contiene:

- CUIL
- APELLIDO_NOMBRE
- CD_OS
- DESCRIPCION O_S
- FECHA_NAC
- EDAD

Regla V1:

- Debe quedar un solo CUIL.
- Los duplicados no se eligen automáticamente; se separan para revisión.

### 3. Actualización de base de Sergio (persistencia)

Entrada esperada: archivo tipo `3 - Devolución_Sergio_...xlsx` con datos enriquecidos.

La aplicación debe:

1. Validar esquema.
2. Cargar en staging.
3. Rechazar filas sin CUIL.
4. Rechazar duplicados por CUIL.
5. Mostrar resumen antes de confirmar.
6. Aplicar actualización masiva sobre DuckDB.

Regla de actualización (autoritativa):

- Si el CUIL no existe: insertar.
- Si el CUIL existe y la columna fuente **no está presente** en el archivo importado: preservar valor existente.
- Si el CUIL existe y la columna fuente está presente con valor **no vacío**: reemplazar valor existente.
- Si el CUIL existe y la columna fuente está presente pero **vacía**: limpiar valor existente.
- Actualizar `fecha_actualizacion` cuando una importación aplica efectivamente esa fila.

Fuente de persistencia definitiva:

- La base `personas` se actualiza a partir de la devolución enriquecida de Sergio.
- El archivo crudo de Hernán se usa para preparación/generación del pedido, no como fuente final de persistencia.

### 4. Consulta de datos guardados de Sergio

La aplicación debe permitir filtros por cualquier columna disponible.

Filtros prioritarios en interfaz:

- Obra social.
- Código postal.
- Edad.

Otros filtros posibles:

- Provincia.
- Localidad.
- Partido.
- Sexo.
- Disponibilidad de celular.
- Disponibilidad de WhatsApp.
- Cualquier columna canónica del modelo.

### 5. Exportar para clientes/call centers

La aplicación debe:

1. Permitir aplicar filtros.
2. Mostrar cantidad resultante.
3. Permitir elegir columnas visibles/exportadas.
4. Exportar siempre como CSV.

V1 no necesita renombrar columnas; el usuario podrá hacerlo manualmente después si hace falta.

### 6. Stock y extracciones (Proceso 4)

Objetivo:

- Operar campañas sobre un stock de trabajo derivado de `personas`, sin eliminar la base madre.

Capacidades implementadas:

1. Selector de fecha con últimas 5 `fecha_importacion`.
2. Generar stock para fecha seleccionada.
3. Regenerar stock con confirmación explícita de acción destructiva sobre stock actual.
4. Marcado de stock desactualizado (stale) si no corresponde al último import exitoso.
5. Exportar resumen CSV (agregado por grupo).
6. Exportar stock completo CSV (detalle por persona) con opción sólo disponibles.
7. Extraer selección por cantidades por grupo con confirmación.
8. Recuperar pendiente por token (`Retry` o `Cancel`).
9. Reexportar extracciones completadas del stock vigente.

Reglas de operación:

- Regenerar stock:
  - reemplaza miembros del stock operativo,
  - limpia pendiente,
  - elimina continuidad del historial operativo de extracción/reexportación previo.
- Extracción multi-grupo: validación de disponibilidad por grupo y reserva/finalización con semántica todo-o-nada (sin ventas parciales confirmadas).
- Reexportación: sólo tokens presentes en el stock actual.

Reglas de seguridad/privacidad:

- `personas` (base madre) no se borra desde este proceso.
- No se registran valores de PII por fila en consola de actividad.

Formato de salida:

- CSV únicamente.
- UTF-8 con BOM.
- Delimitador fijo coma en V1.
- Delimitador configurable queda fuera de alcance.

## Validaciones obligatorias

Las reglas de columnas desconocidas son distintas por etapa; no aplicar la misma política a los dos procesos.

### Generales (ambas etapas)

| Validación | Resultado si falla |
|---|---|
| Archivo inexistente o ilegible | Detener y mostrar error. |
| CUIL vacío | Separar a rechazados. |
| CUIL duplicado dentro del lote | Separar duplicados a revisión/rechazo. |
| CUIL malformado | Separar a rechazados. |
| Archivo corrupto | Detener, loguear error y recomendar recuperar desde backup si corresponde. |

### Proceso de datos de Hernán (preparación)

| Validación | Resultado si falla |
|---|---|
| Faltan columnas mínimas para generar el pedido a Sergio (`CUIL`, `CUIL_APENOM`, `CUIL_CODOS`, `CUIL_DESCRIPOS`, `CUIL_FECHANAC`, `CUIL_EDAD`) | Detener y listar diferencias. |
| Columna desconocida/adicional en el archivo de Hernán | **No bloquea.** Se registra como aviso (notice) informativo; no requiere revisión previa a continuar esta etapa. |

### Actualización de base de Sergio (persistencia)

| Validación | Resultado si falla |
|---|---|
| Esquema de columnas distinto del esperado | Detener y listar diferencias. |
| Columna desconocida/nueva en la devolución de Sergio (no existe en el esquema canónico actual) | **Bloquea** (error de validación). No se ignora en silencio ni se crea automáticamente. Requiere revisión humana de significado, tipo y reglas de actualización, y una migración versionada antes de aceptarla. *(Pendiente de implementar.)* |
| Columna opcional conocida ausente | No es un error: se preserva el valor existente (contrato por columna). |

Validación estructural de CUIL (ambas etapas):

- Se acepta CUIL en 11 dígitos ASCII (`0-9`) o con espacios/guiones que normalicen a 11 dígitos.
- Se rechaza vacío, caracteres no permitidos o longitud distinta de 11 tras normalizar.
- La validación de dígito verificador (checksum argentino) queda diferida a un work unit posterior.

## Staging y seguridad operativa

Cada importación masiva debe usar staging:

1. Cargar archivo en tabla temporal.
2. Validar estructura y calidad mínima.
3. Calcular resumen.
4. Pedir confirmación.
5. Aplicar cambios a tabla principal.

Antes de confirmar, mostrar advertencia:

> Se recomienda crear un backup antes de continuar. Podés seguir sin hacerlo.

## Resumen previo de importación

Antes de confirmar una importación, mostrar:

- Archivo seleccionado.
- Etapa detectada.
- Total de filas leídas.
- Registros válidos.
- Registros nuevos.
- Registros a actualizar.
- CUIL vacíos.
- CUIL duplicados.
- Rechazados totales.
- Tiempo estimado o estado del análisis.

## Logs

La aplicación debe mantener logs locales con:

- Fecha y hora.
- Acción ejecutada.
- Archivo procesado.
- Etapa.
- Conteos principales.
- Duración.
- Resultado: OK / advertencia / error.
- Mensaje técnico del error si existe.

Regla de privacidad:

- No registrar datos personales completos innecesarios.
- Los datos problemáticos deben ir principalmente al archivo de rechazados, no al log general.

## Backups

Backups V1:

- Manuales mediante botón `Crear backup`.
- Destino elegible por el usuario, por ejemplo carpeta dentro de Dropbox.
- Nombre sugerido: `PapaPersonas_yyyyMMdd_HHmm.duckdb`.
- Mostrar fecha del último backup realizado.

Regla técnica:

- No copiar la base mientras hay una importación activa.
- Ejecutar checkpoint/cierre controlado antes de copiar.
- Copiar una instantánea de la base, no trabajar directamente sobre la copia de Dropbox.

## Modelo de datos canónico inicial

Tabla principal sugerida: `personas`.

Campos mínimos iniciales:

- `cuil` — clave primaria.
- `dni`.
- `fecha_nacimiento`.
- `sexo`.
- `tipo_dni`.
- `apellido`.
- `nombre`.
- `direccion`.
- `codigo_postal`.
- `localidad`.
- `partido`.
- `provincia`.
- `nacionalidad`.
- `telefono_fijo_1` a `telefono_fijo_5`.
- `celular_1` a `celular_5`.
- `whatsapp_1` a `whatsapp_5`.
- `email_1` a `email_5`.
- `codigo_obra_social`.
- `obra_social`.
- `cuit_empleador`.
- `edad`.
- `anio`.
- `fecha_actualizacion`.

### Unión canónica de columnas observadas (V1)

- Template Workbook 0: **36** columnas (incluye `ANIO`).
- Muestra real Workbook 3 (devolución): **28** columnas.
- Decisión V1: usar la unión más amplia conocida de columnas y preservar el contrato de presencia de columnas por importación.

## Mapeo de columnas observado

| Concepto canónico | Archivo Hernán | Archivo Sergio enriquecido | Salida cliente |
|---|---|---|---|
| CUIL | CUIL | CUIL | CUIL |
| Nombre completo | CUIL_APENOM | — | — |
| Apellido | — | APELLIDO | APELLIDO |
| Nombre | — | NOMBRE | NOMBRE |
| Fecha nacimiento | CUIL_FECHANAC | FECNANAC | — |
| Edad | CUIL_EDAD | EDAD | — |
| Código obra social | CUIL_CODOS | CODIGOOS | COD_O_SOCIAL |
| Obra social | CUIL_DESCRIPOS | OBRASOCIAL | DESCRIPCION O_SOCIAL |
| Código postal | CUIL_CP | CP | CODPOSTAL |
| Localidad | CUIL_LOCALIDAD | LOCALIDAD | LOCALIDAD |
| Partido | — | PARTIDO | PARTIDO |
| Provincia | CUIL_PROVINCIA | PROVINCIA | PROVINCIA |
| Celulares | — | CELULAR1..5 | CELU 1..5 |
| WhatsApp | — | WSP1..5 | — |
| Emails | CUIL_EMAIL | EMAIL1..3 | — |

## Arquitectura técnica

### Componentes

| Componente | Responsabilidad |
|---|---|
| WPF UI | Pantallas, selección de archivos, progreso, confirmaciones. |
| Application Services | Orquestar importación, validación, exportación y backup. |
| DuckDB Repository | Consultas, staging, upsert, exportación CSV. |
| File Services | Lectura de archivos, escritura de rechazados, backups. |
| Logging | Registro local de acciones y errores. |

### Base de datos

- Motor: DuckDB.
- Acceso: DuckDB.NET.
- Archivo local: `data/PapaPersonas.duckdb`.
- Escritura: sólo desde la aplicación.
- Patrón principal: cargas masivas, consultas y exportaciones.

### Distribución

- Aplicación Windows offline.
- Publicación autocontenida.
- Instalable/copiadable en la PC base.
- Evitar depender de SQL Server, Python o navegador.

## Pantallas V1

1. **Inicio / Estado**
   - Total de personas.
   - Fecha de última importación.
   - Fecha de último backup.
   - Accesos a importar, consultar, exportar y backup.

2. **Proceso de datos de Hernán**
   - Seleccionar archivo.
   - Validar.
   - Ver resumen.
   - Generar archivo para Sergio.

3. **Actualización de base de Sergio**
   - Seleccionar archivo.
   - Validar staging.
   - Ver resumen.
   - Confirmar actualización.

4. **Consulta de datos guardados de Sergio**
   - Filtros por columnas.
   - Selección de columnas a exportar.
   - Vista de conteo resultante.
   - Exportar CSV.

5. **Stock y extracciones**
   - Seleccionar fecha de importación (últimas 5).
   - Generar/regenerar stock operativo.
   - Exportar resumen/completo.
   - Extraer selección por grupos.
   - Resolver pendientes por token (Retry/Cancel).
   - Reexportar extracciones del stock vigente.

6. **Backup**
   - Crear backup manual.
   - Elegir carpeta destino.
   - Ver último backup.

7. **Logs / actividad**
   - Últimas acciones.
   - Errores recientes.
   - Ubicación de archivos de log.

## Estado UX WPF aprobado (implementado)

- Navegación principal por `TabControl` superior con un solo proceso visible a la vez:
  1. Preparar datos de Hernán (habilitado)
  2. Actualizar base de Sergio (habilitado)
  3. Consultar datos (habilitado, V1 guiada para filtros simples y exportación CSV)
  4. Stock y extracciones (habilitado, V1 stock operativo)
- Se eliminan tarjetas redundantes de procesos en pantalla principal.
- `MainWindow` queda como shell liviano: título, tabs, estado DB, protección de cierre y coordinación global de busy.
- Proceso 1 y Proceso 2 se extraen a `UserControl`s separados para mantenibilidad.

### Consolas de actividad por proceso

- Cada proceso tiene su propia consola inferior, con scroll vertical/horizontal y fuente monoespaciada.
- Consola con acciones `Copy` y `Clear`.
- Auto-scroll al último mensaje.
- Estado solo en sesión de app (sin persistencia a archivos/logs).
- Cambio de tabs mantiene el historial de cada consola mientras la app permanezca abierta.
- Nueva ejecución de Proceso 1 limpia solo consola de Proceso 1.
- En Proceso 2:
  - `Analyze` inicia nueva ejecución y limpia consola.
  - `Apply` posterior agrega mensajes a esa misma consola.
  - Un nuevo `Analyze` vuelve a limpiar la consola.
- Capado de memoria: máximo 500 mensajes por consola para evitar crecimiento no acotado.
- Mensajería de hitos/agregados/errores/rutas; no se registran valores por fila ni PII en consola.

## Criterios de aceptación V1

- [ ] La aplicación abre offline en Windows 11.
- [ ] Puede crear/abrir una base DuckDB local.
- [x] Valida encabezados por etapa: mínimos de Hernán con avisos no bloqueantes ante columnas extra; columnas desconocidas de Sergio con Continuar/Cancelar y bloqueos estructurales, antes de staging.
- [x] Valida estructura de CUIL (11 dígitos normalizado) en Core y staging. *(Checksum diferido.)*
- [ ] Ante una columna desconocida/nueva en la devolución de Sergio, bloquea y requiere revisión humana antes de incorporarla vía migración versionada. *(Regla de negocio confirmada; falta conectar la validación de Core al flujo real de importación.)*
- [ ] Rechaza filas sin CUIL.
- [ ] Rechaza CUIL duplicados del lote.
- [x] Genera archivo para Sergio desde el archivo de Hernán.
- [x] Importa devolución de Sergio en staging (Proceso 2A preview sin apply).
- [ ] Muestra resumen antes de aplicar cambios.
- [x] Inserta nuevos CUIL (Proceso 2B backend).
- [x] Aplica contrato por columna para CUIL existente: ausente preserva, presente no vacío reemplaza, presente vacío limpia (Proceso 2B backend).
- [x] Guarda fecha de actualización por registro (Proceso 2B backend).
- [ ] Permite filtrar por cualquier columna disponible.
- [ ] Permite elegir columnas de salida.
- [ ] Exporta CSV.
- [ ] Genera backup manual consistente.
- [ ] Guarda logs de acciones y errores.

## Riesgos y decisiones pendientes

| Riesgo / Pendiente | Impacto | Decisión sugerida |
|---|---|---|
| Excel grande puede ser pesado de leer desde .NET | Performance | Preferir convertir/importar por streaming o usar DuckDB cuando sea posible. |
| Archivos con columnas cambiadas | Corrupción de datos | Si falta una columna mínima esperada: detener importación y mostrar diferencias. Columna desconocida/adicional en Hernán: aviso no bloqueante. Columna desconocida/nueva en Sergio: listar en orden de origen y permitir Continuar/Cancelar antes de crear estado; Continuar procesa sólo reconocidas y nunca crea columnas DuckDB. CUIL faltante, encabezados duplicados y colisiones canónicas siguen bloqueando. |
| Duplicados por CUIL | Datos ambiguos | Rechazar a archivo de revisión en V1. |
| Backup manual olvidado | Pérdida ante error humano | Mostrar fecha de último backup y advertencia antes de importar. |
| Base activa en Dropbox | Riesgo de lock/copia inconsistente | Base local; Dropbox sólo como destino de copia. |

## Próximo paso

Conectar Paso 1 a WPF y avanzar con Proceso 2 (actualización estricta de base desde devolución de Sergio hacia DuckDB).
