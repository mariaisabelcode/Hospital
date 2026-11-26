using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<HospitalDb>(o => o.UseSqlite("Data Source=hospital.db"));
var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<HospitalDb>().Database.EnsureCreated();

app.UseDefaultFiles();
app.UseStaticFiles();

// ================= PACIENTES =================
var pac = app.MapGroup("/api/pacientes");

pac.MapGet("/", async (HospitalDb db, string? busca) =>
{
    var q = db.Pacientes.AsQueryable();
    if (!string.IsNullOrWhiteSpace(busca))
        q = q.Where(p => EF.Functions.Like(p.Nome, $"%{busca}%") || p.Cpf.Contains(busca));
    return await q.OrderBy(p => p.Nome).ToListAsync();
});

pac.MapPost("/", async (Paciente p, HospitalDb db) =>
{
    p.Cpf = Validacao.SoDigitos(p.Cpf);
    var erro = Validacao.Paciente(p);
    if (erro is not null) return Results.BadRequest(erro);
    if (await db.Pacientes.AnyAsync(x => x.Cpf == p.Cpf))
        return Results.Conflict("Este CPF já está cadastrado.");

    p.Id = 0;
    db.Pacientes.Add(p);
    await db.SaveChangesAsync();
    return Results.Created($"/api/pacientes/{p.Id}", p);
});

pac.MapPut("/{id}", async (int id, Paciente d, HospitalDb db) =>
{
    var p = await db.Pacientes.FindAsync(id);
    if (p is null) return Results.NotFound();
    d.Cpf = Validacao.SoDigitos(d.Cpf);
    var erro = Validacao.Paciente(d);
    if (erro is not null) return Results.BadRequest(erro);
    if (await db.Pacientes.AnyAsync(x => x.Cpf == d.Cpf && x.Id != id))
        return Results.Conflict("Este CPF já está cadastrado.");

    p.Nome = d.Nome; p.Cpf = d.Cpf; p.DataNascimento = d.DataNascimento;
    p.Telefone = d.Telefone; p.TipoSanguineo = d.TipoSanguineo; p.Alergias = d.Alergias;
    await db.SaveChangesAsync();
    return Results.Ok(p);
});

pac.MapDelete("/{id}", async (int id, HospitalDb db) =>
{
    var p = await db.Pacientes.FindAsync(id);
    if (p is null) return Results.NotFound();
    if (await db.Consultas.AnyAsync(c => c.PacienteId == id))
        return Results.Conflict("Paciente possui consultas e não pode ser excluído.");
    db.Pacientes.Remove(p);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ================= MÉDICOS =================
var med = app.MapGroup("/api/medicos");

med.MapGet("/", async (HospitalDb db) => await db.Medicos.OrderBy(m => m.Nome).ToListAsync());

med.MapPost("/", async (Medico m, HospitalDb db) =>
{
    if (string.IsNullOrWhiteSpace(m.Nome) || string.IsNullOrWhiteSpace(m.Crm) || string.IsNullOrWhiteSpace(m.Especialidade))
        return Results.BadRequest("Nome, CRM e especialidade são obrigatórios.");
    if (await db.Medicos.AnyAsync(x => x.Crm == m.Crm))
        return Results.Conflict("Este CRM já está cadastrado.");

    m.Id = 0;
    db.Medicos.Add(m);
    await db.SaveChangesAsync();
    return Results.Created($"/api/medicos/{m.Id}", m);
});

med.MapPut("/{id}", async (int id, Medico d, HospitalDb db) =>
{
    var m = await db.Medicos.FindAsync(id);
    if (m is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(d.Nome) || string.IsNullOrWhiteSpace(d.Crm) || string.IsNullOrWhiteSpace(d.Especialidade))
        return Results.BadRequest("Nome, CRM e especialidade são obrigatórios.");
    if (await db.Medicos.AnyAsync(x => x.Crm == d.Crm && x.Id != id))
        return Results.Conflict("Este CRM já está cadastrado.");

    m.Nome = d.Nome; m.Crm = d.Crm; m.Especialidade = d.Especialidade;
    await db.SaveChangesAsync();
    return Results.Ok(m);
});

med.MapDelete("/{id}", async (int id, HospitalDb db) =>
{
    var m = await db.Medicos.FindAsync(id);
    if (m is null) return Results.NotFound();
    if (await db.Consultas.AnyAsync(c => c.MedicoId == id))
        return Results.Conflict("Médico possui consultas e não pode ser excluído.");
    db.Medicos.Remove(m);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ================= CONSULTAS =================
var con = app.MapGroup("/api/consultas");

con.MapGet("/", async (HospitalDb db) =>
    await db.Consultas
        .OrderBy(c => c.DataHora)
        .Select(c => new
        {
            c.Id, c.PacienteId, c.MedicoId,
            Paciente = c.Paciente!.Nome,
            Medico = c.Medico!.Nome,
            c.DataHora, c.Motivo, c.Status
        })
        .ToListAsync());

con.MapPost("/", async (Consulta c, HospitalDb db) =>
{
    c.Status = "Agendada";
    var erro = await ValidarConsulta(c, db, null);
    if (erro is not null) return erro;

    c.Id = 0;
    db.Consultas.Add(c);
    await db.SaveChangesAsync();
    return Results.Created($"/api/consultas/{c.Id}", new { c.Id });
});

con.MapPut("/{id}", async (int id, Consulta d, HospitalDb db) =>
{
    var c = await db.Consultas.FindAsync(id);
    if (c is null) return Results.NotFound();
    var erro = await ValidarConsulta(d, db, id);
    if (erro is not null) return erro;

    c.PacienteId = d.PacienteId; c.MedicoId = d.MedicoId;
    c.DataHora = d.DataHora; c.Motivo = d.Motivo; c.Status = d.Status;
    await db.SaveChangesAsync();
    return Results.Ok();
});
// Repare: não existe DELETE de consulta. Para desfazer, mude o status para "Cancelada".

static async Task<IResult?> ValidarConsulta(Consulta c, HospitalDb db, int? idAtual)
{
    if (!await db.Pacientes.AnyAsync(p => p.Id == c.PacienteId))
        return Results.BadRequest("Paciente não encontrado.");
    if (!await db.Medicos.AnyAsync(m => m.Id == c.MedicoId))
        return Results.BadRequest("Médico não encontrado.");
    if (c.Status is not ("Agendada" or "Realizada" or "Cancelada"))
        return Results.BadRequest("Status inválido.");

    if (c.Status == "Agendada")
    {
        if (idAtual is null && c.DataHora < DateTime.Now)
            return Results.BadRequest("Não é possível agendar no passado.");

        var ini = c.DataHora.AddMinutes(-30);
        var fim = c.DataHora.AddMinutes(30);
        var ocupado = await db.Consultas.AnyAsync(x =>
            x.MedicoId == c.MedicoId && x.Id != idAtual && x.Status == "Agendada" &&
            x.DataHora > ini && x.DataHora < fim);
        if (ocupado)
            return Results.Conflict("O médico já tem uma consulta nesse horário (intervalo de 30 min).");
    }
    return null;
}

app.Run();

// ================= MODELOS =================
class Paciente
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public DateOnly DataNascimento { get; set; }
    public string? Telefone { get; set; }
    public string? TipoSanguineo { get; set; }
    public string? Alergias { get; set; }
}

class Medico
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Crm { get; set; } = "";
    public string Especialidade { get; set; } = "";
}

class Consulta
{
    public int Id { get; set; }
    public int PacienteId { get; set; }
    public int MedicoId { get; set; }
    public DateTime DataHora { get; set; }
    public string? Motivo { get; set; }
    public string Status { get; set; } = "Agendada";
    public Paciente? Paciente { get; set; }
    public Medico? Medico { get; set; }
}

class HospitalDb : DbContext
{
    public HospitalDb(DbContextOptions<HospitalDb> options) : base(options) { }
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<Medico> Medicos => Set<Medico>();
    public DbSet<Consulta> Consultas => Set<Consulta>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Impede que apagar um paciente/médico apague as consultas em cascata
        b.Entity<Consulta>().HasOne(c => c.Paciente).WithMany().HasForeignKey(c => c.PacienteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Consulta>().HasOne(c => c.Medico).WithMany().HasForeignKey(c => c.MedicoId).OnDelete(DeleteBehavior.Restrict);
    }
}

static class Validacao
{
    public static string SoDigitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    public static string? Paciente(Paciente p)
    {
        if (string.IsNullOrWhiteSpace(p.Nome)) return "Nome é obrigatório.";
        if (!CpfValido(p.Cpf)) return "CPF inválido.";
        if (p.DataNascimento == default || p.DataNascimento > DateOnly.FromDateTime(DateTime.Today))
            return "Data de nascimento inválida.";
        return null;
    }

    public static bool CpfValido(string cpf)
    {
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;
        for (int t = 9; t < 11; t++)
        {
            int soma = 0;
            for (int i = 0; i < t; i++) soma += (cpf[i] - '0') * (t + 1 - i);
            if (cpf[t] - '0' != soma * 10 % 11 % 10) return false;
        }
        return true;
    }
}