const $ = (selector) => document.querySelector(selector);
const dateField = $('#selected-date');
const form = $('#booking-form');
const notice = $('#notice');
const todayUtc = () => new Date().toISOString().slice(0, 10);
dateField.value = todayUtc();
form.elements.date.value = dateField.value;

function showNotice(message, isError = false) {
  notice.textContent = message;
  notice.hidden = false;
  notice.classList.toggle('error', isError);
}

async function api(url, options) {
    let response;
    try {
        response = await fetch(url, options);
    } catch {
        throw new Error('Could not reach the server. Please try again.');
    }
  const body = response.status === 204 ? null : await response.json().catch(() => null);
   if (!response.ok) {
        if (typeof body?.message !== 'string' || body?.message.length === 0) {
            throw new Error(`An internal error occurred on the server. Please try again.`);
        }

   const fieldMessages = body?.errors ? Object.values(body.errors).flat().join(' ') : '';

   throw new Error([body?.message, fieldMessages].filter(Boolean).join(' ')
      || `Request failed (${response.status}).`);
  }
  return body;
}

async function loadRoomsAndBookings() {
  try {
    const [rooms, bookings] = await Promise.all([
      api('/api/rooms'),
      api(`/api/bookings?date=${encodeURIComponent(dateField.value)}`)
    ]);
    const select = form.elements.roomId;
    const prior = select.value;
    select.replaceChildren(...rooms.map(room => new Option(room.name, room.id)));
    if (rooms.some(room => String(room.id) === prior)) select.value = prior;
    const grid = $('#rooms');
    grid.replaceChildren(...rooms.map((room, index) => {
      const card = document.createElement('article');
      card.className = 'room-card';
      const heading = document.createElement('div');
      heading.className = 'room-head';
      heading.innerHTML = `<h3></h3><span class="room-number">ROOM 0${index + 1}</span>`;
      heading.querySelector('h3').textContent = room.name;
      card.append(heading);
      const roomBookings = bookings.filter(b => b.roomId === room.id);
      if (!roomBookings.length) {
        const empty = document.createElement('p'); empty.className = 'empty'; empty.textContent = 'Nothing booked yet.'; card.append(empty);
      }
      for (const booking of roomBookings) {
        const item = document.createElement('div'); item.className = 'booking';
        const start = new Date(booking.startUtc).toISOString().slice(11, 16);
        const end = new Date(booking.endUtc).toISOString().slice(11, 16);
        const time = document.createElement('time'); time.textContent = `${start} – ${end}`;
        const who = document.createElement('span'); who.innerHTML = '<span></span><small>BOOKED BY</small>'; who.querySelector('span').textContent = booking.name;
        const cancel = document.createElement('button'); cancel.className = 'cancel'; cancel.type = 'button'; cancel.title = 'Cancel booking'; cancel.setAttribute('aria-label', `Cancel booking for ${booking.name}`); cancel.textContent = '×';
        cancel.addEventListener('click', async () => {
          try { await api(`/api/bookings/${booking.id}`, { method: 'DELETE' }); showNotice('Booking cancelled. The time is available again.'); await loadRoomsAndBookings(); }
          catch (error) { showNotice(error.message, true); }
        });
        item.append(time, who, cancel); card.append(item);
      }
      return card;
    }));
  } catch (error) { showNotice(error.message, true); }
}

dateField.addEventListener('change', () => { form.elements.date.value = dateField.value; loadRoomsAndBookings(); });
form.elements.date.addEventListener('change', () => { dateField.value = form.elements.date.value; loadRoomsAndBookings(); });
form.addEventListener('submit', async event => {
  event.preventDefault();
  const payload = Object.fromEntries(new FormData(form).entries());
  payload.roomId = Number(payload.roomId);
  try {
    await api('/api/bookings', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    showNotice('Room booked. Your reservation is on the schedule.');
    await loadRoomsAndBookings();
    form.elements.name.value = '';
  } catch (error) { showNotice(error.message, true); }
});

loadRoomsAndBookings();
