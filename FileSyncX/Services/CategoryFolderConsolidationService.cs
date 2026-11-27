using System.Data;
using System.Data.Common;
using FileSyncX.EfCore.Contexts;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FileSyncX.Services;

/// <summary>
/// Consolida destinos de categorias retornadas exclusivamente pela consulta
/// de similaridade (>= 80%) em pastas diferentes.
///
/// As categorias secundárias NÃO são excluídas e os documentos NÃO têm
/// IdHashDefault alterado. Somente o destino é redirecionado para a pasta
/// principal, preservando a identidade semântica original para auditoria,
/// tratamento de erros e eventual reversão.
/// </summary>
public sealed class CategoryFolderConsolidationService
    : ICategoryFolderConsolidationService
{
    private const int LimiteResultados = 200;

    private const string SqlCandidatas = """
        WITH categorias AS MATERIALIZED
        (
            SELECT
                h.id,
                COALESCE(
                    NULLIF(
                        CASE
                            WHEN h.description ILIKE 'Categoria:%' THEN
                                BTRIM(
                                    SPLIT_PART(
                                        SUBSTRING(h.description FROM 11),
                                        E'\n',
                                        1
                                    )
                                )
                            ELSE
                                CASE
                                    WHEN CHAR_LENGTH(BTRIM(SPLIT_PART(h.description, E'\n', 1))) > 90
                                        THEN LEFT(BTRIM(SPLIT_PART(h.description, E'\n', 1)), 87) || '...'
                                    ELSE BTRIM(SPLIT_PART(h.description, E'\n', 1))
                                END
                        END,
                        ''
                    ),
                    'Categoria ' || h.id::text
                ) AS nome_categoria,
                h.destination_folder,
                LOWER(
                    RTRIM(
                        REPLACE(BTRIM(h.destination_folder), '/', E'\\'),
                        E'\\'
                    )
                ) AS pasta_normalizada,
                h.embedding
            FROM public.tbl_hash_default h
            WHERE h.embedding IS NOT NULL
              AND h.id = ANY(@ids)
        ),
        pares AS MATERIALIZED
        (
            SELECT
                a.id AS id_categoria_a,
                a.nome_categoria AS categoria_a,
                a.destination_folder AS pasta_a,
                b.id AS id_categoria_b,
                b.nome_categoria AS categoria_b,
                b.destination_folder AS pasta_b,
                vizinho.distancia_cosseno,
                1.0 - vizinho.distancia_cosseno AS similaridade
            FROM categorias a
            CROSS JOIN LATERAL
            (
                SELECT
                    h.id,
                    h.embedding <=> a.embedding AS distancia_cosseno
                FROM public.tbl_hash_default h
                WHERE h.embedding IS NOT NULL
                  AND h.id = ANY(@ids)
                  AND LOWER(
                        RTRIM(
                            REPLACE(BTRIM(h.destination_folder), '/', E'\\'),
                            E'\\'
                        )
                      ) IS DISTINCT FROM a.pasta_normalizada
                ORDER BY h.embedding <=> a.embedding
                LIMIT 200
            ) AS vizinho
            INNER JOIN categorias b
                ON b.id = vizinho.id
               AND b.id > a.id
            WHERE vizinho.distancia_cosseno <= 0.20
        ),
        pares_limitados AS MATERIALIZED
        (
            SELECT *
            FROM pares
            ORDER BY similaridade DESC, id_categoria_a, id_categoria_b
            LIMIT 200
        )
        SELECT
            p.id_categoria_a,
            p.categoria_a,
            COALESCE(da.total_documentos, 0)::integer AS documentos_a,
            p.pasta_a,
            p.id_categoria_b,
            p.categoria_b,
            COALESCE(db.total_documentos, 0)::integer AS documentos_b,
            p.pasta_b,
            p.similaridade,
            p.distancia_cosseno,
            CASE
                WHEN COALESCE(da.total_documentos, 0) > COALESCE(db.total_documentos, 0)
                    THEN p.id_categoria_a
                WHEN COALESCE(db.total_documentos, 0) > COALESCE(da.total_documentos, 0)
                    THEN p.id_categoria_b
                ELSE LEAST(p.id_categoria_a, p.id_categoria_b)
            END AS id_categoria_principal,
            CASE
                WHEN COALESCE(da.total_documentos, 0) > COALESCE(db.total_documentos, 0)
                    THEN p.id_categoria_b
                WHEN COALESCE(db.total_documentos, 0) > COALESCE(da.total_documentos, 0)
                    THEN p.id_categoria_a
                ELSE GREATEST(p.id_categoria_a, p.id_categoria_b)
            END AS id_categoria_secundaria,
            CASE
                WHEN COALESCE(da.total_documentos, 0) > COALESCE(db.total_documentos, 0)
                    THEN p.pasta_a
                WHEN COALESCE(db.total_documentos, 0) > COALESCE(da.total_documentos, 0)
                    THEN p.pasta_b
                WHEN p.id_categoria_a < p.id_categoria_b
                    THEN p.pasta_a
                ELSE p.pasta_b
            END AS pasta_principal
        FROM pares_limitados p
        LEFT JOIN LATERAL
        (
            SELECT COUNT(*)::integer AS total_documentos
            FROM public.tbl_archive_hash_categorization d
            WHERE d.id_hash_default = p.id_categoria_a
        ) da ON TRUE
        LEFT JOIN LATERAL
        (
            SELECT COUNT(*)::integer AS total_documentos
            FROM public.tbl_archive_hash_categorization d
            WHERE d.id_hash_default = p.id_categoria_b
        ) db ON TRUE
        ORDER BY p.similaridade DESC, p.id_categoria_a, p.id_categoria_b;
        """;

    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;

    public CategoryFolderConsolidationService(
        IDbContextFactory<VecktorVaultDbContext> dbFactory)
    {
        _dbFactory = dbFactory
            ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<CategoryFolderSearchResult> BuscarCandidatasAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        DbConnection connection = db.Database.GetDbConnection();

        // Primeiro consulta o catálogo e valida a existência física dos diretórios.
        // A consulta vetorial só recebe IDs cujos destinos existem e são diretórios
        // reais (não arquivos, junctions ou links simbólicos).
        var idsComPastaFisica = new List<int>();
        var categoriasSemPastaFisica = new HashSet<int>();
        var caminhosIndisponiveis = new List<string>();
        int categoriasVerificadas = 0;

        await using (DbCommand pastasCommand = connection.CreateCommand())
        {
            pastasCommand.CommandType = CommandType.Text;
            pastasCommand.CommandTimeout = 30;
            pastasCommand.CommandText = """
                SELECT id, destination_folder
                FROM public.tbl_hash_default
                WHERE embedding IS NOT NULL
                ORDER BY id;
                """;

            await using DbDataReader pastasReader =
                await pastasCommand.ExecuteReaderAsync(cancellationToken);

            while (await pastasReader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                categoriasVerificadas++;

                int id = pastasReader.GetInt32(0);
                string? caminho = pastasReader.IsDBNull(1)
                    ? null
                    : pastasReader.GetString(1);

                if (TryValidarPastaFisica(caminho, out string motivo))
                {
                    idsComPastaFisica.Add(id);
                }
                else
                {
                    categoriasSemPastaFisica.Add(id);
                    if (caminhosIndisponiveis.Count < 20)
                    {
                        string exibicao = string.IsNullOrWhiteSpace(caminho)
                            ? "<destination_folder vazio>"
                            : caminho;
                        caminhosIndisponiveis.Add(
                            $"Categoria {id}: {exibicao} — {motivo}");
                    }
                }
            }
        }

        if (idsComPastaFisica.Count < 2)
        {
            return new CategoryFolderSearchResult(
                [],
                categoriasVerificadas,
                idsComPastaFisica.Count,
                categoriasSemPastaFisica.Count,
                caminhosIndisponiveis);
        }

        // Os parâmetros HNSW ficam restritos à transação desta consulta.
        await using IDbContextTransaction efTransaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        DbTransaction transaction = efTransaction.GetDbTransaction();

        await using (DbCommand settingsCommand = connection.CreateCommand())
        {
            settingsCommand.Transaction = transaction;
            settingsCommand.CommandType = CommandType.Text;
            settingsCommand.CommandText = """
                SELECT
                    set_config('hnsw.ef_search', '200', true),
                    set_config('hnsw.iterative_scan', 'strict_order', true),
                    set_config('hnsw.max_scan_tuples', '50000', true),
                    set_config('hnsw.scan_mem_multiplier', '2', true);
                """;
            settingsCommand.CommandTimeout = 30;
            await settingsCommand.ExecuteScalarAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SqlCandidatas;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 120;

        DbParameter idsParameter = command.CreateParameter();
        idsParameter.ParameterName = "ids";
        idsParameter.Value = idsComPastaFisica.ToArray();
        command.Parameters.Add(idsParameter);

        var resultado = new List<CategoryFolderMergeCandidate>(LimiteResultados);

        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                int idA = reader.GetInt32(0);
                string nomeA = reader.GetString(1);
                int documentosA = reader.GetInt32(2);
                string? pastaA = reader.IsDBNull(3) ? null : reader.GetString(3);

                int idB = reader.GetInt32(4);
                string nomeB = reader.GetString(5);
                int documentosB = reader.GetInt32(6);
                string? pastaB = reader.IsDBNull(7) ? null : reader.GetString(7);

                // Revalida os caminhos lidos na consulta. Isso cobre uma alteração
                // de destination_folder ou remoção de diretório ocorrida durante
                // a pesquisa inicial.
                bool pastaAValida = TryValidarPastaFisica(pastaA, out string motivoA);
                bool pastaBValida = TryValidarPastaFisica(pastaB, out string motivoB);
                if (!pastaAValida || !pastaBValida)
                {
                    if (!pastaAValida && categoriasSemPastaFisica.Add(idA) &&
                        caminhosIndisponiveis.Count < 20)
                    {
                        caminhosIndisponiveis.Add(
                            $"Categoria {idA}: {pastaA ?? "<destination_folder nulo>"} — {motivoA}");
                    }

                    if (!pastaBValida && categoriasSemPastaFisica.Add(idB) &&
                        caminhosIndisponiveis.Count < 20)
                    {
                        caminhosIndisponiveis.Add(
                            $"Categoria {idB}: {pastaB ?? "<destination_folder nulo>"} — {motivoB}");
                    }

                    continue;
                }

                // A validação acima garante strings não nulas.
                string caminhoAValidado = pastaA!;
                string caminhoBValidado = pastaB!;

                double similaridade = reader.GetDouble(8);
                double distancia = reader.GetDouble(9);
                int idPrincipal = reader.GetInt32(10);
                int idSecundaria = reader.GetInt32(11);
                // A pasta principal é uma das duas pastas já validadas acima.
                string pastaPrincipal = idPrincipal == idA
                    ? caminhoAValidado
                    : caminhoBValidado;

                resultado.Add(new CategoryFolderMergeCandidate(
                    idA,
                    nomeA,
                    documentosA,
                    caminhoAValidado,
                    idB,
                    nomeB,
                    documentosB,
                    caminhoBValidado,
                    similaridade,
                    distancia,
                    idPrincipal,
                    idSecundaria,
                    pastaPrincipal));
            }
        }

        await efTransaction.CommitAsync(cancellationToken);

        int categoriasComPastaFisicaFinal = idsComPastaFisica.Count(
            id => !categoriasSemPastaFisica.Contains(id));

        return new CategoryFolderSearchResult(
            resultado,
            categoriasVerificadas,
            categoriasComPastaFisicaFinal,
            categoriasSemPastaFisica.Count,
            caminhosIndisponiveis);
    }

    public async Task<CategoryFolderConsolidationResult> ConsolidarAsync(
        IReadOnlyList<CategoryFolderMergeCandidate> candidatas,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatas);

        // IMPORTANT: processa exatamente os pares que o usuário visualizou e
        // confirmou. Não executa novamente a busca HNSW aqui: uma nova busca
        // aproximada pode retornar vizinhos diferentes e descartar todos os
        // pares da prévia, produzindo uma operação aparentemente bem-sucedida
        // que não move nenhum arquivo.
        var pares = candidatas
            .GroupBy(x => (
                A: Math.Min(x.IdCategoriaA, x.IdCategoriaB),
                B: Math.Max(x.IdCategoriaA, x.IdCategoriaB)))
            .Select(g => g.First())
            .OrderByDescending(x => x.Similaridade)
            .ToList();

        int consolidados = 0;
        int ignorados = 0;
        int arquivosMovidos = 0;
        int documentosAtualizados = 0;
        var erros = new List<string>();
        var avisos = new List<string>();
        var categoriasProcessadas = new HashSet<int>();

        foreach (CategoryFolderMergeCandidate candidata in pares)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (categoriasProcessadas.Contains(candidata.IdCategoriaA) ||
                categoriasProcessadas.Contains(candidata.IdCategoriaB))
            {
                ignorados++;
                avisos.Add(
                    $"Par {candidata.IdCategoriaA}/{candidata.IdCategoriaB} ignorado: " +
                    "uma das categorias já foi utilizada em outro par nesta execução.");
                continue;
            }

            try
            {
                ConsolidacaoParResult resultado = await ConsolidarParAsync(
                    candidata,
                    cancellationToken);

                if (!resultado.Consolidado)
                {
                    ignorados++;
                    if (!string.IsNullOrWhiteSpace(resultado.Motivo))
                        avisos.Add(resultado.Motivo);
                    continue;
                }

                consolidados++;
                arquivosMovidos += resultado.ArquivosMovidos;
                documentosAtualizados += resultado.DocumentosAtualizados;
                categoriasProcessadas.Add(candidata.IdCategoriaA);
                categoriasProcessadas.Add(candidata.IdCategoriaB);

                if (!string.IsNullOrWhiteSpace(resultado.Aviso))
                    avisos.Add(resultado.Aviso);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                erros.Add(
                    $"Categorias {candidata.IdCategoriaA} e {candidata.IdCategoriaB}: " +
                    ex.GetBaseException().Message);
            }
        }

        return new CategoryFolderConsolidationResult(
            pares.Count,
            consolidados,
            ignorados,
            arquivosMovidos,
            documentosAtualizados,
            erros)
        {
            Avisos = avisos
        };
    }

    private async Task<ConsolidacaoParResult> ConsolidarParAsync(
        CategoryFolderMergeCandidate candidata,
        CancellationToken cancellationToken)
    {
        // Mantém a contagem no escopo do método inteiro, pois é incrementada
        // durante a transação e utilizada no retorno após o bloco try/catch.
        int documentosAtualizados = 0;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);

        await using IDbContextTransaction efTransaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        DbConnection connection = db.Database.GetDbConnection();
        DbTransaction transaction = efTransaction.GetDbTransaction();

        // Bloqueia as duas categorias em ordem de ID para evitar deadlocks.
        var categorias = await ObterCategoriasBloqueadasAsync(
            connection,
            transaction,
            candidata.IdCategoriaA,
            candidata.IdCategoriaB,
            cancellationToken);

        if (!categorias.TryGetValue(candidata.IdCategoriaPrincipalSugerida, out CategoriaDestino? principal) ||
            !categorias.TryGetValue(candidata.IdCategoriaSecundariaSugerida, out CategoriaDestino? secundaria))
        {
            return ConsolidacaoParResult.Ignorado(
                $"Par {candidata.IdCategoriaA}/{candidata.IdCategoriaB} ignorado: " +
                "uma das categorias não existe mais no banco.");
        }

        if (string.IsNullOrWhiteSpace(principal.DestinationFolder) ||
            string.IsNullOrWhiteSpace(secundaria.DestinationFolder))
        {
            throw new InvalidOperationException(
                "Uma das categorias possui destination_folder vazio.");
        }

        string pastaPrincipal = Path.GetFullPath(principal.DestinationFolder);
        string pastaSecundaria = Path.GetFullPath(secundaria.DestinationFolder);

        // A prévia não pode ser aplicada sobre caminhos que mudaram depois
        // da confirmação. Isso evita redirecionar uma categoria diferente.
        string pastaPrincipalDaPrevia = candidata.IdCategoriaPrincipalSugerida == candidata.IdCategoriaA
            ? candidata.PastaA
            : candidata.PastaB;
        string pastaSecundariaDaPrevia = candidata.IdCategoriaSecundariaSugerida == candidata.IdCategoriaA
            ? candidata.PastaA
            : candidata.PastaB;

        if (!PathsEquivalentes(pastaPrincipal, pastaPrincipalDaPrevia) ||
            !PathsEquivalentes(pastaSecundaria, pastaSecundariaDaPrevia))
        {
            return ConsolidacaoParResult.Ignorado(
                $"Par {candidata.IdCategoriaA}/{candidata.IdCategoriaB} ignorado: " +
                "o destino de uma categoria mudou desde a análise. Atualize a análise.");
        }

        if (PathsEquivalentes(pastaPrincipal, pastaSecundaria))
        {
            return ConsolidacaoParResult.Ignorado(
                $"Par {candidata.IdCategoriaA}/{candidata.IdCategoriaB} ignorado: " +
                "as categorias já apontam para a mesma pasta.");
        }

        if (IsSameOrChildPath(pastaPrincipal, pastaSecundaria) ||
            IsSameOrChildPath(pastaSecundaria, pastaPrincipal))
        {
            throw new InvalidOperationException(
                "As pastas são hierarquicamente relacionadas. O par foi bloqueado por segurança.");
        }

        // Revalida a similaridade do par exato (não faz uma nova busca HNSW).
        // É uma comparação entre dois IDs conhecidos, portanto não exige
        // varredura da tabela nem depende da aproximação do índice.
        double? similaridadeAtual = await ObterSimilaridadeParAsync(
            connection,
            transaction,
            candidata.IdCategoriaA,
            candidata.IdCategoriaB,
            cancellationToken);

        if (similaridadeAtual is null || similaridadeAtual.Value < 0.80d)
        {
            return ConsolidacaoParResult.Ignorado(
                $"Par {candidata.IdCategoriaA}/{candidata.IdCategoriaB} ignorado: " +
                "a similaridade atual está abaixo de 80% ou um embedding foi removido.");
        }

        // A consolidação só é válida se AMBOS os diretórios existirem fisicamente.
        // Não cria uma pasta principal ausente e não atualiza o banco quando
        // qualquer destino estiver ausente, inacessível ou for um link/junction.
        if (!TryValidarPastaFisica(pastaPrincipal, out string motivoPrincipal))
        {
            throw new DirectoryNotFoundException(
                $"A pasta principal da categoria {principal.Id} não está disponível: " +
                $"'{pastaPrincipal}'. {motivoPrincipal} Nenhum destino foi alterado no banco.");
        }

        if (!TryValidarPastaFisica(pastaSecundaria, out string motivoSecundaria))
        {
            throw new DirectoryNotFoundException(
                $"A pasta secundária da categoria {secundaria.Id} não está disponível: " +
                $"'{pastaSecundaria}'. {motivoSecundaria} Nenhum destino foi alterado no banco.");
        }

        GarantirNaoSerPontoDeReanalise(pastaSecundaria);
        GarantirNaoSerPontoDeReanalise(pastaPrincipal);

        var documentos = await ObterDocumentosAsync(
            connection,
            transaction,
            candidata.IdCategoriaSecundariaSugerida,
            cancellationToken);

        var arquivosMovidos = new List<(string Origem, string Destino)>();
        var mapaCaminhos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Revalidação imediatamente antes da movimentação física para
            // fechar a janela entre a análise e o início da operação.
            bool pastaPrincipalContinuaValida =
                TryValidarPastaFisica(pastaPrincipal, out string motivoPrincipalAtual);
            bool pastaSecundariaContinuaValida =
                TryValidarPastaFisica(pastaSecundaria, out string motivoSecundariaAtual);

            if (!pastaPrincipalContinuaValida || !pastaSecundariaContinuaValida)
            {
                throw new DirectoryNotFoundException(
                    "Um dos diretórios deixou de existir ou ficou indisponível antes da movimentação. " +
                    $"Principal: {motivoPrincipalAtual} Secundária: {motivoSecundariaAtual} " +
                    "Nenhuma alteração no banco será confirmada.");
            }

            foreach (string arquivoOrigem in EnumerarArquivosSemLinks(pastaSecundaria))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativo = Path.GetRelativePath(pastaSecundaria, arquivoOrigem);
                string destinoPretendido = Path.Combine(pastaPrincipal, relativo);
                string destino = ObterDestinoDisponivel(destinoPretendido);
                string? diretorioDestino = Path.GetDirectoryName(destino);

                if (!string.IsNullOrWhiteSpace(diretorioDestino))
                    Directory.CreateDirectory(diretorioDestino);

                File.Move(arquivoOrigem, destino);
                arquivosMovidos.Add((arquivoOrigem, destino));
                mapaCaminhos[NormalizarCaminho(arquivoOrigem)] = destino;
            }

            foreach (DocumentoDestino documento in documentos)
            {
                string? novoArquivoDestino = null;

                if (!string.IsNullOrWhiteSpace(documento.DestinationFile) &&
                    mapaCaminhos.TryGetValue(
                        NormalizarCaminho(documento.DestinationFile),
                        out string? destinoPorDestinationFile))
                {
                    novoArquivoDestino = destinoPorDestinationFile;
                }
                else if (!string.IsNullOrWhiteSpace(documento.SourceFile) &&
                         mapaCaminhos.TryGetValue(
                             NormalizarCaminho(documento.SourceFile),
                             out string? destinoPorSourceFile))
                {
                    novoArquivoDestino = destinoPorSourceFile;
                }

                await AtualizarDocumentoAsync(
                    connection,
                    transaction,
                    documento.Id,
                    pastaPrincipal,
                    novoArquivoDestino,
                    cancellationToken);

                documentosAtualizados++;
            }

            await AtualizarDestinoCategoriaAsync(
                connection,
                transaction,
                candidata.IdCategoriaSecundariaSugerida,
                pastaPrincipal,
                cancellationToken);

            await efTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await efTransaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserva a exceção original.
            }

            RestaurarArquivosMovidos(arquivosMovidos);
            throw;
        }

        // A limpeza ocorre somente depois do commit. Remove apenas diretórios
        // vazios; links simbólicos, arquivos não movidos e outros conteúdos
        // residuais impedem a exclusão e são preservados.
        string? avisoLimpeza;
        try
        {
            avisoLimpeza = RemoverDiretoriosVazios(pastaSecundaria)
                ? null
                : $"A categoria foi redirecionada, mas restaram itens na pasta secundária: {pastaSecundaria}";
        }
        catch (Exception ex)
        {
            avisoLimpeza =
                $"Os arquivos e o banco foram consolidados, mas não foi possível remover " +
                $"a estrutura vazia de '{pastaSecundaria}': {ex.GetBaseException().Message}";
        }

        return new ConsolidacaoParResult(
            true,
            arquivosMovidos.Count,
            documentosAtualizados,
            null,
            avisoLimpeza);
    }

    private static async Task<Dictionary<int, CategoriaDestino>> ObterCategoriasBloqueadasAsync(
        DbConnection connection,
        DbTransaction transaction,
        int idA,
        int idB,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CriarComando(
            connection,
            transaction,
            """
            SELECT id, destination_folder
            FROM public.tbl_hash_default
            WHERE id IN (@id_a, @id_b)
            ORDER BY id
            FOR UPDATE;
            """);

        AdicionarParametro(command, "@id_a", idA);
        AdicionarParametro(command, "@id_b", idB);

        var categorias = new Dictionary<int, CategoriaDestino>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int id = reader.GetInt32(0);
            string destino = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            categorias[id] = new CategoriaDestino(id, destino);
        }

        return categorias;
    }

    private static async Task<double?> ObterSimilaridadeParAsync(
        DbConnection connection,
        DbTransaction transaction,
        int idA,
        int idB,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CriarComando(
            connection,
            transaction,
            """
            SELECT 1.0 - (a.embedding <=> b.embedding)
            FROM public.tbl_hash_default a
            INNER JOIN public.tbl_hash_default b ON b.id = @id_b
            WHERE a.id = @id_a
              AND a.embedding IS NOT NULL
              AND b.embedding IS NOT NULL;
            """);

        AdicionarParametro(command, "@id_a", idA);
        AdicionarParametro(command, "@id_b", idB);

        object? valor = await command.ExecuteScalarAsync(cancellationToken);
        if (valor is null || valor is DBNull)
            return null;

        return Convert.ToDouble(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryValidarPastaFisica(string? caminho, out string motivo)
    {
        motivo = string.Empty;

        if (string.IsNullOrWhiteSpace(caminho))
        {
            motivo = "destination_folder está vazio no banco.";
            return false;
        }

        if (!Path.IsPathRooted(caminho.Trim()))
        {
            motivo = "O caminho registrado no banco não é absoluto.";
            return false;
        }

        string caminhoCompleto;
        try
        {
            caminhoCompleto = Path.GetFullPath(caminho.Trim());
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            motivo = "O caminho registrado no banco é inválido: " + ex.Message;
            return false;
        }

        FileAttributes atributos;
        try
        {
            atributos = File.GetAttributes(caminhoCompleto);
        }
        catch (DirectoryNotFoundException)
        {
            motivo = "O diretório não existe fisicamente.";
            return false;
        }
        catch (FileNotFoundException)
        {
            motivo = "O caminho não existe fisicamente.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            motivo = "Sem permissão para verificar o diretório.";
            return false;
        }
        catch (IOException ex)
        {
            motivo = "Não foi possível acessar o caminho: " + ex.Message;
            return false;
        }
        catch (System.Security.SecurityException ex)
        {
            motivo = "Acesso ao caminho bloqueado por segurança: " + ex.Message;
            return false;
        }

        if ((atributos & FileAttributes.Directory) == 0)
        {
            motivo = "O caminho existe, mas aponta para um arquivo, não para uma pasta.";
            return false;
        }

        if ((atributos & FileAttributes.ReparsePoint) != 0)
        {
            motivo = "O caminho é um link simbólico/junction e foi bloqueado por segurança.";
            return false;
        }

        // File.GetAttributes confirma existência; Directory.Exists confirma
        // que o caminho continua sendo enumerável como diretório.
        if (!Directory.Exists(caminhoCompleto))
        {
            motivo = "O diretório não existe ou não está acessível para o processo.";
            return false;
        }

        return true;
    }

    private static void GarantirNaoSerPontoDeReanalise(string pasta)
    {
        FileAttributes atributos = File.GetAttributes(pasta);
        if ((atributos & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"A pasta '{pasta}' é um link/junction. A consolidação foi bloqueada por segurança.");
        }
    }

    private static bool RemoverDiretoriosVazios(string raiz)
    {
        if (!Directory.Exists(raiz))
            return true;

        foreach (string subpasta in Directory.GetDirectories(raiz, "*", SearchOption.TopDirectoryOnly))
        {
            FileAttributes atributos = File.GetAttributes(subpasta);
            if ((atributos & FileAttributes.ReparsePoint) != 0)
                return false;

            if (!RemoverDiretoriosVazios(subpasta))
                return false;
        }

        if (Directory.EnumerateFileSystemEntries(raiz).Any())
            return false;

        Directory.Delete(raiz, false);
        return true;
    }

    private static async Task<List<DocumentoDestino>> ObterDocumentosAsync(
        DbConnection connection,
        DbTransaction transaction,
        int idCategoria,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CriarComando(
            connection,
            transaction,
            """
            SELECT id, source_file, destination_file
            FROM public.tbl_archive_hash_categorization
            WHERE id_hash_default = @id_categoria
            FOR UPDATE;
            """);

        AdicionarParametro(command, "@id_categoria", idCategoria);

        var documentos = new List<DocumentoDestino>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            documentos.Add(new DocumentoDestino(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return documentos;
    }

    private static async Task AtualizarDocumentoAsync(
        DbConnection connection,
        DbTransaction transaction,
        long idDocumento,
        string pastaPrincipal,
        string? novoArquivoDestino,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CriarComando(
            connection,
            transaction,
            """
            UPDATE public.tbl_archive_hash_categorization
            SET destination_folder = @pasta,
                destination_file = CASE
                    WHEN @arquivo IS NULL THEN destination_file
                    ELSE @arquivo
                END
            WHERE id = @id;
            """);

        AdicionarParametro(command, "@pasta", pastaPrincipal);
        AdicionarParametro(command, "@arquivo", novoArquivoDestino);
        AdicionarParametro(command, "@id", idDocumento);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AtualizarDestinoCategoriaAsync(
        DbConnection connection,
        DbTransaction transaction,
        int idCategoria,
        string pastaPrincipal,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CriarComando(
            connection,
            transaction,
            """
            UPDATE public.tbl_hash_default
            SET destination_folder = @pasta
            WHERE id = @id;
            """);

        AdicionarParametro(command, "@pasta", pastaPrincipal);
        AdicionarParametro(command, "@id", idCategoria);

        int afetados = await command.ExecuteNonQueryAsync(cancellationToken);
        if (afetados != 1)
            throw new InvalidOperationException(
                $"Não foi possível redirecionar a categoria {idCategoria}.");
    }

    private static DbCommand CriarComando(
        DbConnection connection,
        DbTransaction transaction,
        string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        return command;
    }

    private static void AdicionarParametro(
        DbCommand command,
        string nome,
        object? valor)
    {
        DbParameter parametro = command.CreateParameter();
        parametro.ParameterName = nome.TrimStart('@');
        if (valor is null)
            parametro.DbType = DbType.String;
        parametro.Value = valor ?? DBNull.Value;
        command.Parameters.Add(parametro);
    }

    private static IEnumerable<string> EnumerarArquivosSemLinks(string raiz)
    {
        var pendentes = new Stack<string>();
        pendentes.Push(raiz);

        while (pendentes.Count > 0)
        {
            string pasta = pendentes.Pop();
            string[] arquivos;
            string[] subpastas;

            try
            {
                arquivos = Directory.GetFiles(pasta, "*", SearchOption.TopDirectoryOnly);
                subpastas = Directory.GetDirectories(pasta, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (IOException)
            {
                throw;
            }

            foreach (string arquivo in arquivos)
                yield return arquivo;

            foreach (string subpasta in subpastas)
            {
                try
                {
                    if ((File.GetAttributes(subpasta) & FileAttributes.ReparsePoint) == 0)
                        pendentes.Push(subpasta);
                }
                catch (IOException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
            }
        }
    }

    private static string ObterDestinoDisponivel(string caminhoPretendido)
    {
        if (!File.Exists(caminhoPretendido) && !Directory.Exists(caminhoPretendido))
            return caminhoPretendido;

        string pasta = Path.GetDirectoryName(caminhoPretendido)!;
        string nome = Path.GetFileNameWithoutExtension(caminhoPretendido);
        string extensao = Path.GetExtension(caminhoPretendido);

        for (int i = 1; ; i++)
        {
            string candidato = Path.Combine(pasta, $"{nome} ({i}){extensao}");
            if (!File.Exists(candidato) && !Directory.Exists(candidato))
                return candidato;
        }
    }

    private static void RestaurarArquivosMovidos(
        IReadOnlyList<(string Origem, string Destino)> arquivos)
    {
        for (int i = arquivos.Count - 1; i >= 0; i--)
        {
            var item = arquivos[i];
            try
            {
                if (File.Exists(item.Destino) && !File.Exists(item.Origem))
                {
                    string? pastaOrigem = Path.GetDirectoryName(item.Origem);
                    if (!string.IsNullOrWhiteSpace(pastaOrigem))
                        Directory.CreateDirectory(pastaOrigem);

                    File.Move(item.Destino, item.Origem);
                }
            }
            catch
            {
                // A exceção original deve ser preservada. O relatório principal
                // identificará a categoria que não concluiu a consolidação.
            }
        }
    }

    private static string NormalizarCaminho(string caminho)
    {
        string absoluto = Path.GetFullPath(caminho);
        string raiz = Path.GetPathRoot(absoluto) ?? string.Empty;
        string semSeparadorFinal = absoluto.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (semSeparadorFinal.Length < raiz.Length)
            semSeparadorFinal = raiz;

        return semSeparadorFinal;
    }

    private static bool PathsEquivalentes(string a, string b)
        => string.Equals(
            NormalizarCaminho(a),
            NormalizarCaminho(b),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChildPath(string parent, string candidate)
    {
        string relative = Path.GetRelativePath(
            NormalizarCaminho(parent),
            NormalizarCaminho(candidate));

        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !relative.StartsWith(
                    ".." + Path.AltDirectorySeparatorChar,
                    StringComparison.Ordinal));
    }

    private sealed record CategoriaDestino(int Id, string DestinationFolder);

    private sealed record DocumentoDestino(
        long Id,
        string? SourceFile,
        string? DestinationFile);

    private sealed record ConsolidacaoParResult(
        bool Consolidado,
        int ArquivosMovidos,
        int DocumentosAtualizados,
        string? Motivo,
        string? Aviso)
    {
        public static ConsolidacaoParResult Ignorado(string motivo)
            => new(false, 0, 0, motivo, null);
    }
}
