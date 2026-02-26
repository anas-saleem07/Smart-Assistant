//const API_BASE = "https://localhost:5001/api/reminder";

//async function loadReminders() {
//    const res = await fetch(`${API_BASE}/list`);
//    const data = await res.json();

//    const list = document.getElementById("reminderList");
//    list.innerHTML = "";

//    data.forEach(r => {
//        list.innerHTML += `
//          <li>
//            ${r.title}
//            <button onclick="deleteReminder(${r.id})">❌</button>
//          </li>`;
//    });
//}

//async function addReminder() {
//    const reminder = {
//        title: title.value,
//        description: description.value,
//        reminderTime: time.value
//    };

//    await fetch(`${API_BASE}/create`, {
//        method: "POST",
//        headers: { "Content-Type": "application/json" },
//        body: JSON.stringify(reminder)
//    AS

//    loadReminders();
//}

//async function deleteReminder(id) {
//    await fetch(`${API_BASE}/delete/${id}`, { method: "DELETE" });
//    loadReminders();
//}

//document.addEventListener("DOMContentLoaded", loadReminders);