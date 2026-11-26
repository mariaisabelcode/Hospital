const fmtCpf = v => v?.replace(/(\d{3})(\d{3})(\d{3})(\d{2})/, "$1.$2.$3-$4");
const fmtData = v => v ? new Date(v + "T00:00").toLocaleDateString("pt-BR") : "";
const fmtDataHora = v => new Date(v).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });

const ENTIDADES = {
  pacientes: {
    titulo: "Pacientes",
    campos: [
      { n: "nome", l: "Nome completo", req: true },
      { n: "cpf", l: "CPF", req: true, ph: "000.000.000-00" },
      { n: "dataNascimento", l: "Data de nascimento", t: "date", req: true },
      { n: "telefone", l: "Telefone", t: "tel" },
      { n: "tipoSanguineo", l: "Tipo sanguíneo", t: "select", opcoes: ["", "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"] },
      { n: "alergias", l: "Alergias" },
    ],
    colunas: [["Nome", "nome"], ["CPF", "cpf", fmtCpf], ["Nascimento", "dataNascimento", fmtData],
              ["Telefone", "telefone"], ["Tipo sang.", "tipoSanguineo"]],
  },
  medicos: {
    titulo: "Médicos",
    campos: [
      { n: "nome", l: "Nome completo", req: true },
      { n: "crm", l: "CRM", req: true, ph: "12345-PR" },
      { n: "especialidade", l: "Especialidade", req: true },
    ],
    colunas: [["Nome", "nome"], ["CRM", "crm"], ["Especialidade", "especialidade"]],
  },
  consultas: {
    titulo: "Consultas",
    semExcluir: true, // consultas são canceladas, não apagadas
    campos: [
      { n: "pacienteId", l: "Paciente", t: "select", de: "pacientes", req: true },
      { n: "medicoId", l: "Médico", t: "select", de: "medicos", req: true },
      { n: "dataHora", l: "Data e hora", t: "datetime-local", req: true },
      { n: "motivo", l: "Motivo" },
      { n: "status", l: "Status", t: "select", opcoes: ["Agendada", "Realizada", "Cancelada"] },
    ],
    colunas: [["Data/hora", "dataHora", fmtDataHora], ["Paciente", "paciente"], ["Médico", "medico"],
              ["Motivo", "motivo"], ["Status", "status"]],
  },
};

const $ = id => document.getElementById(id);
let atual = "pacientes";
let itens = [];

function esc(texto) {
  const d = document.createElement("div");
  d.textContent = texto ?? "";
  return d.innerHTML;
}

const listar = nome => fetch(`/api/${nome}`).then(r => r.json());

// ---------- Formulário ----------
async function montarForm() {
  const e = ENTIDADES[atual];
  let html = '<input type="hidden" id="id">';
  for (const c of e.campos) {
    let input;
    if (c.t === "select") {
      const ops = c.de
        ? (await listar(c.de)).map(x => [x.id, x.nome])
        : c.opcoes.map(o => [o, o]);
      input = `<select id="f_${c.n}" ${c.req ? "required" : ""}>
        ${c.de ? '<option value="">Selecione...</option>' : ""}
        ${ops.map(([v, t]) => `<option value="${esc(v)}">${esc(t)}</option>`).join("")}
      </select>`;
    } else {
      input = `<input id="f_${c.n}" type="${c.t || "text"}" ${c.req ? "required" : ""} placeholder="${c.ph || ""}">`;
    }
    html += `<label>${c.l}${input}</label>`;
  }
  html += `<div class="acoes">
      <button type="submit" id="salvar">Cadastrar</button>
      <button type="button" id="cancelar" class="secundario" hidden>Cancelar edição</button>
    </div>
    <p id="msg" role="alert"></p>`;
  $("form").innerHTML = html;
  $("cancelar").onclick = limpar;
}

function limpar() {
  $("form").reset();
  $("id").value = "";
  $("salvar").textContent = "Cadastrar";
  $("cancelar").hidden = true;
  $("msg").textContent = "";
}

// ---------- Tabela ----------
async function carregar() {
  const e = ENTIDADES[atual];
  itens = await listar(atual);
  $("cabecalho").innerHTML = "<tr>" + e.colunas.map(c => `<th>${c[0]}</th>`).join("") + "<th></th></tr>";
  desenhar();
}

function desenhar() {
  const e = ENTIDADES[atual];
  const termo = $("busca").value.toLowerCase();
  const visiveis = itens.filter(i => JSON.stringify(i).toLowerCase().includes(termo));
  $("tabela").innerHTML = visiveis.map(i => `
    <tr>
      ${e.colunas.map(([, k, f]) => `<td>${esc(f ? f(i[k]) : i[k])}</td>`).join("")}
      <td>
        <button onclick="editar(${i.id})">Editar</button>
        ${e.semExcluir ? "" : `<button class="perigo" onclick="excluir(${i.id})">Excluir</button>`}
      </td>
    </tr>`).join("");
  $("vazio").hidden = visiveis.length > 0;
}

// ---------- Ações ----------
function editar(id) {
  const e = ENTIDADES[atual];
  const item = itens.find(x => x.id === id);
  $("id").value = id;
  for (const c of e.campos) {
    let v = item[c.n] ?? "";
    if (c.t === "datetime-local") v = String(v).slice(0, 16);
    $("f_" + c.n).value = v;
  }
  $("salvar").textContent = "Salvar alterações";
  $("cancelar").hidden = false;
  window.scrollTo({ top: 0, behavior: "smooth" });
}

async function excluir(id) {
  if (!confirm("Excluir este registro?")) return;
  const resp = await fetch(`/api/${atual}/${id}`, { method: "DELETE" });
  if (!resp.ok) alert((await resp.text()).replace(/^"|"$/g, ""));
  carregar();
}

$("form").addEventListener("submit", async ev => {
  ev.preventDefault();
  const e = ENTIDADES[atual];
  const id = $("id").value;
  const dados = {};
  for (const c of e.campos) {
    let v = $("f_" + c.n).value.trim();
    if (c.n.endsWith("Id")) v = Number(v);
    dados[c.n] = v === "" ? null : v;
  }

  const resp = await fetch(id ? `/api/${atual}/${id}` : `/api/${atual}`, {
    method: id ? "PUT" : "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(dados),
  });

  if (!resp.ok) {
    $("msg").textContent = (await resp.text()).replace(/^"|"$/g, "");
    return;
  }
  limpar();
  carregar();
});

// ---------- Navegação ----------
async function trocar(nome) {
  atual = nome;
  $("busca").value = "";
  document.querySelectorAll("#menu button").forEach(b => b.classList.toggle("ativo", b.dataset.k === nome));
  await montarForm();
  await carregar();
}

$("menu").innerHTML = Object.entries(ENTIDADES)
  .map(([k, e]) => `<button data-k="${k}">${e.titulo}</button>`).join("");
$("menu").addEventListener("click", ev => ev.target.dataset.k && trocar(ev.target.dataset.k));
$("busca").addEventListener("input", desenhar);

trocar("pacientes");