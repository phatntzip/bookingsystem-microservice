// Thin client for the AI Orchestration (/ai) and the API Gateway (/gw), both via the Vite proxy.

export async function sendChat(message, conversationId) {
  const res = await fetch('/ai/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, conversationId }),
  });
  if (!res.ok) throw new Error(`Chat failed (HTTP ${res.status}).`);
  return res.json();
}

export async function confirmProposal(conversationId, proposalId, approve) {
  const res = await fetch('/ai/chat/confirm', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ conversationId, proposalId, approve }),
  });
  if (!res.ok) throw new Error(`Confirm failed (HTTP ${res.status}).`);
  return res.json();
}

export async function getCatalogs() {
  const res = await fetch('/gw/api/catalog/catalogs');
  if (!res.ok) throw new Error(`Load catalogs failed (HTTP ${res.status}).`);
  return res.json();
}

export async function getBookings() {
  const res = await fetch('/gw/api/bookings');
  if (!res.ok) throw new Error(`Load bookings failed (HTTP ${res.status}).`);
  return res.json();
}
