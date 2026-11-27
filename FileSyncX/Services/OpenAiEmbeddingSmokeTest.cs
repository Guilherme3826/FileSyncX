
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace FileSyncX.Services;

public static class OpenAiEmbeddingSmokeTest
{
    public static async Task ExecuteAsync(IServiceProvider serviceProvider)
    {
        try
        {
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("INICIANDO TESTE DE EMBEDDING OPENAI");
            Debug.WriteLine("========================================");

            // 1. Consulta a variável nos três escopos do Windows.
            const string nomeVariavel = "OPENAI_API_KEY";

            string? chaveProcesso = Environment.GetEnvironmentVariable(
                nomeVariavel,
                EnvironmentVariableTarget.Process
            );

            string? chaveUsuario = Environment.GetEnvironmentVariable(
                nomeVariavel,
                EnvironmentVariableTarget.User
            );

            string? chaveSistema = Environment.GetEnvironmentVariable(
                nomeVariavel,
                EnvironmentVariableTarget.Machine
            );

            // 2. Identifica a origem da chave, respeitando a prioridade.
            string? origemChave = null;
            string? chaveDisponivel = null;

            if (!string.IsNullOrWhiteSpace(chaveProcesso))
            {
                origemChave = "Process";
                chaveDisponivel = chaveProcesso;
            }
            else if (!string.IsNullOrWhiteSpace(chaveUsuario))
            {
                origemChave = "User";
                chaveDisponivel = chaveUsuario;
            }
            else if (!string.IsNullOrWhiteSpace(chaveSistema))
            {
                origemChave = "Machine";
                chaveDisponivel = chaveSistema;
            }

            // 3. Exibe o diagnóstico sem revelar a chave.
            Debug.WriteLine("");
            Debug.WriteLine("=== DIAGNOSTICO DA OPENAI_API_KEY ===");

            Debug.WriteLine(
                $"Processo: {!string.IsNullOrWhiteSpace(chaveProcesso)}"
            );

            Debug.WriteLine(
                $"Usuario: {!string.IsNullOrWhiteSpace(chaveUsuario)}"
            );

            Debug.WriteLine(
                $"Sistema: {!string.IsNullOrWhiteSpace(chaveSistema)}"
            );

            if (chaveDisponivel is null)
            {
                throw new InvalidOperationException(
                    "A variável OPENAI_API_KEY não foi encontrada " +
                    "em nenhum dos escopos: Process, User ou Machine."
                );
            }

            Debug.WriteLine(
                $"OK: chave encontrada no escopo {origemChave}."
            );

            Debug.WriteLine(
                $"Quantidade de caracteres: {chaveDisponivel.Length}"
            );

            // 4. Recupera o serviço registrado no contêiner de DI.
            var embeddingService =
                serviceProvider.GetRequiredService<IEmbeddingService>();

            Debug.WriteLine("");
            Debug.WriteLine("OK: serviço IEmbeddingService localizado.");

            // 5. Texto utilizado no teste.
            const string textoTeste =
                "Documento de teste sobre análise de arquivos PDF, " +
                "classificação documental e organização de documentos.";

            Debug.WriteLine("");
            Debug.WriteLine("Solicitando embedding à OpenAI...");

            // 6. Envia o texto à API e aguarda o retorno.
            float[] vetor = await embeddingService.GenerateEmbeddingAsync(
                textoTeste
            );

            // 7. Verifica se o retorno é válido.
            if (vetor is null || vetor.Length == 0)
            {
                throw new InvalidOperationException(
                    "A API não retornou um vetor válido."
                );
            }

            Debug.WriteLine("OK: embedding recebido.");

            // 8. Verifica a quantidade de dimensões.
            Debug.WriteLine($"Dimensões recebidas: {vetor.Length}");

            if (vetor.Length != 1536)
            {
                throw new InvalidOperationException(
                    $"Dimensão incorreta. Esperado: 1536. " +
                    $"Recebido: {vetor.Length}."
                );
            }

            // 9. Exibe somente os cinco primeiros valores.
            string primeirosValores = string.Join(
                ", ",
                vetor.Take(5).Select(
                    x => x.ToString(
                        "0.000000",
                        CultureInfo.InvariantCulture
                    )
                )
            );

            Debug.WriteLine(
                $"Primeiros valores: {primeirosValores}"
            );

            // 10. Resultado final.
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("TESTE CONCLUÍDO COM SUCESSO!");
            Debug.WriteLine("========================================");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("FALHA NO TESTE DE EMBEDDING");
            Debug.WriteLine("========================================");

            Debug.WriteLine(ex.ToString());
        }
    }
}
