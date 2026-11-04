using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

public class Otimizador
{
    public const string ChaveCpus = "bst.instance.Pie64.cpus";
    public const string ChaveRam = "bst.instance.Pie64.ram";
    public const string ChaveFps = "bst.instance.Pie64.max_fps";
    const int PrioridadeAlta = 3;

    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    readonly string conf;
    readonly string estado;
    readonly RegistryKey raiz;
    readonly string ifeo;
    readonly string perf;
    readonly string[] processos;

    public Otimizador(string conf, RegistryKey raiz, string ifeo, string[] processos)
    {
        this.conf = conf;
        this.estado = conf + ".antes-da-otimizacao.json";
        this.raiz = raiz;
        this.ifeo = ifeo;
        this.perf = ifeo + @"\PerfOptions";
        this.processos = processos;
    }

    public bool JaOtimizado { get { return File.Exists(estado); } }

    public bool EmuladorAberto
    {
        get { return processos.Any(p => Process.GetProcessesByName(p).Length > 0); }
    }

    public string Ler(string chave)
    {
        return Valor(File.ReadAllText(conf, Utf8), chave);
    }

    public bool PrioridadeEhAlta
    {
        get
        {
            using (var k = raiz.OpenSubKey(perf))
            {
                return k != null && Equals(k.GetValue("CpuPriorityClass"), PrioridadeAlta);
            }
        }
    }

    public void Aplicar(int nucleos)
    {
        var ajustes = new Dictionary<string, string>
        {
            { ChaveCpus, nucleos.ToString() },
            { ChaveRam, "4096" },
            { ChaveFps, "120" }
        };

        ExigirEmuladorFechado();
        string texto = File.ReadAllText(conf, Utf8);
        foreach (var chave in ajustes.Keys)
        {
            if (Valor(texto, chave) == null)
                throw new InvalidOperationException("Chave não encontrada, nada foi alterado: " + chave);
        }

        // Aplicar de novo não pode apagar os valores originais já guardados.
        if (!JaOtimizado)
            GuardarEstado(texto, ajustes.Keys);

        foreach (var a in ajustes)
            texto = Definir(texto, a.Key, a.Value);
        Salvar(texto);

        using (var k = raiz.CreateSubKey(perf))
        {
            k.SetValue("CpuPriorityClass", PrioridadeAlta, RegistryValueKind.DWord);
        }
    }

    public void Restaurar()
    {
        if (!JaOtimizado)
            throw new InvalidOperationException("Nenhuma otimização para restaurar.");
        ExigirEmuladorFechado();

        var e = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(estado));

        string texto = File.ReadAllText(conf, Utf8);
        foreach (DictionaryEntry v in new Hashtable((IDictionary)e["Valores"]))
            texto = Definir(texto, (string)v.Key, (string)v.Value);
        Salvar(texto);

        object anterior = e["PrioridadeAnterior"];
        if (anterior != null)
        {
            using (var k = raiz.CreateSubKey(perf))
            {
                k.SetValue("CpuPriorityClass", Convert.ToInt32(anterior), RegistryValueKind.DWord);
            }
        }
        else
        {
            using (var k = raiz.OpenSubKey(perf, true))
            {
                if (k != null)
                    k.DeleteValue("CpuPriorityClass", false);
            }
        }
        if (!(bool)e["PerfOptionsExistia"])
            raiz.DeleteSubKeyTree(perf, false);
        if (!(bool)e["IfeoExistia"])
            raiz.DeleteSubKeyTree(ifeo, false);

        File.Delete(estado);
    }

    void ExigirEmuladorFechado()
    {
        // O emulador regrava o arquivo ao fechar e desfaria as mudanças.
        if (EmuladorAberto)
            throw new InvalidOperationException("Feche o emulador e tente de novo.");
    }

    void GuardarEstado(string texto, IEnumerable<string> chaves)
    {
        object anterior;
        bool perfExistia;
        using (var k = raiz.OpenSubKey(perf))
        {
            perfExistia = k != null;
            anterior = k == null ? null : k.GetValue("CpuPriorityClass");
        }
        bool ifeoExistia;
        using (var k = raiz.OpenSubKey(ifeo))
        {
            ifeoExistia = k != null;
        }

        var dados = new Dictionary<string, object>
        {
            { "Valores", chaves.ToDictionary(c => c, c => Valor(texto, c)) },
            { "PrioridadeAnterior", anterior },
            { "PerfOptionsExistia", perfExistia },
            { "IfeoExistia", ifeoExistia }
        };
        File.WriteAllText(estado, new JavaScriptSerializer().Serialize(dados), Utf8);
    }

    void Salvar(string texto)
    {
        // A troca atômica evita deixar a configuração pela metade se algo falhar.
        string tmp = conf + ".tmp";
        File.WriteAllText(tmp, texto, Utf8);
        File.Replace(tmp, conf, conf + ".bak");
    }

    static Regex Padrao(string chave)
    {
        return new Regex("^" + Regex.Escape(chave) + "=\"([^\"]*)\"", RegexOptions.Multiline);
    }

    static string Valor(string texto, string chave)
    {
        var m = Padrao(chave).Match(texto);
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Definir(string texto, string chave, string valor)
    {
        return Padrao(chave).Replace(texto, _ => chave + "=\"" + valor + "\"");
    }
}

public class Janela : Form
{
    readonly Otimizador otimizador;
    readonly Label status = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
    readonly ComboBox nucleos = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    readonly Button otimizar = new Button { Text = "Otimizar", AutoSize = true };
    readonly Button restaurar = new Button { Text = "Restaurar original", AutoSize = true };

    public Janela(Otimizador otimizador)
    {
        this.otimizador = otimizador;

        Text = "Otimizador MSI";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;

        nucleos.Items.AddRange(new object[] { 4, 6 });
        nucleos.SelectedItem = 4;

        var linhaNucleos = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        linhaNucleos.Controls.Add(new Label { Text = "Núcleos ao otimizar:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        linhaNucleos.Controls.Add(nucleos);

        var botoes = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        botoes.Controls.Add(otimizar);
        botoes.Controls.Add(restaurar);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(16),
            WrapContents = false
        };
        layout.Controls.Add(status);
        layout.Controls.Add(linhaNucleos);
        layout.Controls.Add(botoes);
        Controls.Add(layout);

        otimizar.Click += (s, e) => Executar(() => otimizador.Aplicar((int)nucleos.SelectedItem),
            "Otimização aplicada. Vale na próxima vez que abrir o emulador.");
        restaurar.Click += (s, e) => Executar(otimizador.Restaurar, "Configuração original restaurada.");
        Activated += (s, e) => Atualizar();
        Atualizar();
    }

    void Executar(Action acao, string sucesso)
    {
        try
        {
            acao();
            MessageBox.Show(this, sucesso, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Atualizar();
    }

    void Atualizar()
    {
        try
        {
            status.Text = "Instância: MSI App Player\r\n"
                + "Núcleos: " + otimizador.Ler(Otimizador.ChaveCpus) + "\r\n"
                + "RAM: " + otimizador.Ler(Otimizador.ChaveRam) + " MB\r\n"
                + "Limite de FPS: " + otimizador.Ler(Otimizador.ChaveFps) + "\r\n"
                + "Prioridade: " + (otimizador.PrioridadeEhAlta ? "Alta" : "Normal") + "\r\n\r\n"
                + (otimizador.EmuladorAberto ? "Emulador aberto: feche-o para aplicar." : "Emulador fechado: pronto para aplicar.");
            otimizar.Enabled = !otimizador.EmuladorAberto;
            restaurar.Enabled = otimizador.JaOtimizado && !otimizador.EmuladorAberto;
        }
        catch (Exception ex)
        {
            status.Text = "Não foi possível ler a configuração:\r\n" + ex.Message;
            otimizar.Enabled = false;
            restaurar.Enabled = false;
        }
    }
}

public static class Programa
{
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new Janela(new Otimizador(
            @"C:\ProgramData\BlueStacks_msi5\bluestacks.conf",
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\HD-Player.exe",
            new[] { "HD-Player", "HD-MultiInstanceManager" })));
    }
}
