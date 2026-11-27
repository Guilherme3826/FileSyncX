
using System.Diagnostics;
using System.IO;

using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Services;

public static class OpenAiVecktorVaultSmokeTest
{
    public static async Task ExecuteAsync(
        IServiceProvider serviceProvider)
    {
        try
        {
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("TESTE DE INTEGRACAO OPENAI + VECKTORVAULT");
            Debug.WriteLine("========================================");

            var vault = serviceProvider
                .GetRequiredService<IVecktorVaultService>();

            // =================================================
            // 1. CRIAR CATEGORIA DE REFERÊNCIA
            // =================================================

            const string descricaoCategoria =
                "[TESTE FILESYNCX] Contratos de locação e arrendamento " +
                "de imóveis. Instrumentos particulares com informações " +
                "sobre locador, locatário, aluguel, prazo contratual, " +
                "garantias, direitos e obrigações das partes.";

            string pastaCategoria = Path.Combine(
                Path.GetTempPath(),
                "FileSyncX",
                "Teste",
                "Contratos"
            );

            Debug.WriteLine("");
            Debug.WriteLine("1. Criando categoria de referência...");

            int categoriaId = await vault.CriarOuObterCategoriaAsync(
                descricaoCategoria,
                pastaCategoria
            );

            Debug.WriteLine($"Categoria cadastrada. ID: {categoriaId}");

            // =================================================
            // 2. GERAR EMBEDDING E SALVAR DOCUMENTO
            // =================================================

            const string textoDocumento =
                "Instrumento particular de contrato de locação de imóvel. " +
                "O locador concede ao locatário o direito de utilizar o " +
                "imóvel mediante pagamento mensal de aluguel, conforme " +
                "prazo e condições estabelecidos no contrato.";

            const string arquivoTeste =
                "[TESTE FILESYNCX] contrato_locacao_teste.pdf";

            Debug.WriteLine("");
            Debug.WriteLine("2. Gerando embedding e salvando documento...");

            long documentoId = await vault.IndexarDocumentoAsync(
                arquivoTeste,
                textoDocumento
            );

            Debug.WriteLine(
                $"Documento persistido. ID: {documentoId}"
            );

            // =================================================
            // 3. RECUPERAR DOCUMENTO DO POSTGRESQL
            // =================================================

            Debug.WriteLine("");
            Debug.WriteLine("3. Recuperando documento do PostgreSQL...");

            var documento = await vault.ObterDocumentoAsync(documentoId);

            if (documento is null)
            {
                throw new InvalidOperationException(
                    "Documento não foi encontrado após a gravação."
                );
            }

            if (documento.Embedding is null)
            {
                throw new InvalidOperationException(
                    "O documento foi recuperado, mas o embedding está nulo."
                );
            }

            int dimensoes = documento.Embedding.ToArray().Length;

            Debug.WriteLine($"Documento recuperado: {documento.SourceFile}");
            Debug.WriteLine($"Embedding recuperado: {dimensoes} dimensões");

            if (dimensoes != 1536)
            {
                throw new InvalidOperationException(
                    $"Quantidade de dimensões inesperada: {dimensoes}."
                );
            }

            // =================================================
            // 4. BUSCA POR SIMILARIDADE
            // =================================================

            Debug.WriteLine("");
            Debug.WriteLine("4. Consultando categorias semelhantes...");

            var resultados = await vault.BuscarCategoriasSemelhantesAsync(
                textoDocumento,
                quantidade: 3
            );

            foreach (var resultado in resultados)
            {
                Debug.WriteLine(
                    $"Categoria: {resultado.Description}"
                );

                Debug.WriteLine(
                    $"Similaridade: {resultado.Similarity:P2}"
                );

                Debug.WriteLine(
                    $"Distância cosseno: {resultado.CosineDistance:F6}"
                );
            }

            // =================================================
            // 5. CLASSIFICAR DOCUMENTO
            // =================================================

            Debug.WriteLine("");
            Debug.WriteLine("5. Classificando documento...");

            // 0.75 é apenas um limiar inicial para este teste.
            var melhorCategoria = await vault.ClassificarDocumentoAsync(
                documentoId,
                similaridadeMinima: 0.75
            );

            var documentoClassificado =
                await vault.ObterDocumentoAsync(documentoId);

            if (melhorCategoria is null)
            {
                Debug.WriteLine(
                    "Nenhuma categoria de referência foi encontrada."
                );
            }
            else
            {
                Debug.WriteLine(
                    $"Categoria sugerida: {melhorCategoria.Description}"
                );

                Debug.WriteLine(
                    $"Similaridade final: {melhorCategoria.Similarity:P2}"
                );

                Debug.WriteLine(
                    $"Status: {documentoClassificado!.ClassificationStatus}"
                );
            }

            // =================================================
            // RESULTADO FINAL
            // =================================================

            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("TESTE VECKTORVAULT CONCLUÍDO!");
            Debug.WriteLine("========================================");

            Debug.WriteLine($"ID do documento: {documentoId}");
            Debug.WriteLine($"ID da categoria: {categoriaId}");
            Debug.WriteLine($"Dimensões persistidas: {dimensoes}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("FALHA NO TESTE VECKTORVAULT");
            Debug.WriteLine("========================================");

            Debug.WriteLine(ex.ToString());
        }
    }
}
