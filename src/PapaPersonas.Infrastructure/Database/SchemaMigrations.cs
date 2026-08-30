namespace PapaPersonas.Infrastructure.Database;

internal static class SchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new(
            2,
            "Create canonical personas, import_runs, and personas_staging schema",
            """
            CREATE TABLE IF NOT EXISTS personas (
                cuil VARCHAR PRIMARY KEY,
                dni VARCHAR,
                fecha_nacimiento DATE,
                sexo VARCHAR,
                tipo_dni VARCHAR,
                apellido VARCHAR,
                nombre VARCHAR,
                direccion VARCHAR,
                codigo_postal VARCHAR,
                localidad VARCHAR,
                partido VARCHAR,
                provincia VARCHAR,
                nacionalidad VARCHAR,
                telefono_fijo_1 VARCHAR,
                telefono_fijo_2 VARCHAR,
                telefono_fijo_3 VARCHAR,
                telefono_fijo_4 VARCHAR,
                telefono_fijo_5 VARCHAR,
                celular_1 VARCHAR,
                celular_2 VARCHAR,
                celular_3 VARCHAR,
                celular_4 VARCHAR,
                celular_5 VARCHAR,
                whatsapp_1 VARCHAR,
                whatsapp_2 VARCHAR,
                whatsapp_3 VARCHAR,
                whatsapp_4 VARCHAR,
                whatsapp_5 VARCHAR,
                email_1 VARCHAR,
                email_2 VARCHAR,
                email_3 VARCHAR,
                email_4 VARCHAR,
                email_5 VARCHAR,
                codigo_obra_social VARCHAR,
                obra_social VARCHAR,
                cuit_empleador VARCHAR,
                edad SMALLINT,
                fecha_actualizacion TIMESTAMP NOT NULL
            );

            CREATE TABLE IF NOT EXISTS import_runs (
                import_id UUID PRIMARY KEY,
                stage_type VARCHAR NOT NULL,
                source_file_name VARCHAR NOT NULL,
                source_file_path VARCHAR NOT NULL,
                status VARCHAR NOT NULL DEFAULT 'pending',
                started_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                analyzed_utc TIMESTAMP,
                completed_utc TIMESTAMP,
                total_rows BIGINT NOT NULL DEFAULT 0,
                valid_rows BIGINT NOT NULL DEFAULT 0,
                rejected_rows BIGINT NOT NULL DEFAULT 0,
                missing_cuil_rows BIGINT NOT NULL DEFAULT 0,
                duplicate_cuil_rows BIGINT NOT NULL DEFAULT 0,
                malformed_cuil_rows BIGINT NOT NULL DEFAULT 0,
                rows_to_insert BIGINT NOT NULL DEFAULT 0,
                rows_to_update BIGINT NOT NULL DEFAULT 0,
                error_message VARCHAR,
                CHECK (stage_type IN ('hernan_raw', 'sergio_return')),
                CHECK (status IN ('pending', 'analyzing', 'ready_for_confirmation', 'applying', 'completed', 'failed'))
            );

            CREATE TABLE IF NOT EXISTS personas_staging (
                import_id UUID NOT NULL,
                source_row_number BIGINT NOT NULL,
                cuil VARCHAR,
                dni VARCHAR,
                fecha_nacimiento DATE,
                sexo VARCHAR,
                tipo_dni VARCHAR,
                apellido VARCHAR,
                nombre VARCHAR,
                direccion VARCHAR,
                codigo_postal VARCHAR,
                localidad VARCHAR,
                partido VARCHAR,
                provincia VARCHAR,
                nacionalidad VARCHAR,
                telefono_fijo_1 VARCHAR,
                telefono_fijo_2 VARCHAR,
                telefono_fijo_3 VARCHAR,
                telefono_fijo_4 VARCHAR,
                telefono_fijo_5 VARCHAR,
                celular_1 VARCHAR,
                celular_2 VARCHAR,
                celular_3 VARCHAR,
                celular_4 VARCHAR,
                celular_5 VARCHAR,
                whatsapp_1 VARCHAR,
                whatsapp_2 VARCHAR,
                whatsapp_3 VARCHAR,
                whatsapp_4 VARCHAR,
                whatsapp_5 VARCHAR,
                email_1 VARCHAR,
                email_2 VARCHAR,
                email_3 VARCHAR,
                email_4 VARCHAR,
                email_5 VARCHAR,
                codigo_obra_social VARCHAR,
                obra_social VARCHAR,
                cuit_empleador VARCHAR,
                edad SMALLINT,
                fecha_actualizacion TIMESTAMP,
                validation_outcome VARCHAR NOT NULL DEFAULT 'pending',
                validation_error VARCHAR,
                CHECK (validation_outcome IN ('pending', 'valid', 'rejected')),
                PRIMARY KEY (import_id, source_row_number),
                FOREIGN KEY (import_id) REFERENCES import_runs (import_id)
            );

            CREATE INDEX IF NOT EXISTS idx_personas_obra_social ON personas (obra_social);
            CREATE INDEX IF NOT EXISTS idx_personas_codigo_postal ON personas (codigo_postal);
            CREATE INDEX IF NOT EXISTS idx_personas_edad ON personas (edad);

            CREATE INDEX IF NOT EXISTS idx_import_runs_started_utc ON import_runs (started_utc);
            CREATE INDEX IF NOT EXISTS idx_personas_staging_import_validation ON personas_staging (import_id, validation_outcome);
            """),
        new(
            3,
            "Add ANIO support and source column contract metadata",
            """
            ALTER TABLE personas
            ADD COLUMN IF NOT EXISTS anio SMALLINT;

            ALTER TABLE personas_staging
            ADD COLUMN IF NOT EXISTS anio SMALLINT;

            ALTER TABLE import_runs
            ADD COLUMN IF NOT EXISTS source_columns_present_json VARCHAR;

            UPDATE import_runs
            SET source_columns_present_json = '[]'
            WHERE source_columns_present_json IS NULL;
            """),
        new(
            4,
            "Add import date semantics and legacy backfill",
            """
            ALTER TABLE personas
            ADD COLUMN IF NOT EXISTS fecha_importacion DATE;

            ALTER TABLE import_runs
            ADD COLUMN IF NOT EXISTS fecha_importacion DATE;

            UPDATE personas
            SET fecha_importacion = DATE '2026-08-10'
            WHERE fecha_importacion IS NULL
              AND (SELECT COUNT(*) FROM personas) = 819530
              AND (SELECT COUNT(*) FROM personas WHERE fecha_importacion IS NOT NULL) = 0;
            """),
        new(
            5,
            "Add source lineage metadata to personas and create stock snapshot tables",
            """
            ALTER TABLE personas
            ADD COLUMN IF NOT EXISTS source_import_id UUID;

            ALTER TABLE personas
            ADD COLUMN IF NOT EXISTS source_row_number BIGINT;

            CREATE TABLE IF NOT EXISTS stock_headers (
                stock_id UUID PRIMARY KEY,
                source_fecha_importacion DATE NOT NULL,
                source_import_id UUID,
                generated_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                pending_extraction_token UUID,
                pending_started_utc TIMESTAMP,
                pending_output_path VARCHAR,
                pending_expected_rows BIGINT,
                pending_selected_columns_json VARCHAR,
                CHECK (pending_expected_rows IS NULL OR pending_expected_rows >= 0),
                CHECK (
                    (
                        pending_extraction_token IS NULL
                        AND pending_started_utc IS NULL
                        AND pending_output_path IS NULL
                        AND pending_expected_rows IS NULL
                        AND pending_selected_columns_json IS NULL
                    )
                    OR
                    (
                        pending_extraction_token IS NOT NULL
                        AND pending_started_utc IS NOT NULL
                        AND pending_output_path IS NOT NULL
                        AND pending_expected_rows IS NOT NULL
                        AND pending_selected_columns_json IS NOT NULL
                    )
                )
            );

            CREATE TABLE IF NOT EXISTS stock_members (
                stock_id UUID NOT NULL,
                cuil VARCHAR NOT NULL,
                codigo_obra_social VARCHAR,
                obra_social VARCHAR,
                source_order BIGINT NOT NULL,
                vendido BOOLEAN NOT NULL DEFAULT FALSE,
                fecha_venta TIMESTAMP,
                extraction_token UUID,
                PRIMARY KEY (stock_id, cuil),
                FOREIGN KEY (stock_id) REFERENCES stock_headers (stock_id),
                CHECK (
                    (vendido = FALSE AND fecha_venta IS NULL)
                    OR
                    (vendido = TRUE AND fecha_venta IS NOT NULL)
                )
            );

            CREATE INDEX IF NOT EXISTS idx_stock_members_stock_extraction_token
            ON stock_members (stock_id, extraction_token);
            """)
    ];
}
