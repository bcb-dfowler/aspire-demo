// analytics-svc, Node.js edition. A single, dependency-free file run directly in a stock
// `node` container (no build, no npm install) - see the AddContainer("analytics-svc", "node", ...)
// wiring in src/AspireDemo.AppHost/AppHost.cs.
//
// It's the Node replacement for the former .NET AspireDemo.AnalyticsService: a Dapr bindings.kafka
// *input* binding consumer that aggregates order events. The sidecar (analytics-svc-dapr) owns the
// Kafka connection and POSTs each message here; this app just keeps a running total in memory.
//
// Contract (PascalCase JSON, to match the .NET producer and the Kafka binding payload):
//   POST /order-events   body {"OrderId": string, "Amount": number} -> 200, records Amount
//   OPTIONS /order-events -> 200 (daprd's startup probe for input-binding routes)
//   GET  /stats          -> 200 {"OrderCount": <int>, "TotalRevenue": <number>}
// State is in-memory and resets on restart - same as the .NET version. Node is single-threaded, so
// no locking is needed around the aggregate.

const http = require('http');

const PORT = 8080;

let orderCount = 0;
let totalRevenue = 0;

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; });
    req.on('end', () => {
      if (raw.length === 0) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(raw));
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

const server = http.createServer(async (req, res) => {
  const { method, url } = req;

  // daprd probes input-binding routes with OPTIONS at startup - must answer 2xx or the app
  // channel is considered not ready and messages are never delivered.
  if (method === 'OPTIONS' && url === '/order-events') {
    res.writeHead(200).end();
    return;
  }

  if (method === 'POST' && url === '/order-events') {
    try {
      const event = await readJsonBody(req);
      const amount = Number(event.Amount);
      totalRevenue += Number.isFinite(amount) ? amount : 0;
      orderCount += 1;
      console.log(`Recorded order ${event.OrderId ?? '(no id)'} amount=${amount} -> count=${orderCount} revenue=${totalRevenue}`);
      res.writeHead(200).end();
    } catch (err) {
      console.error('Failed to parse order event:', err.message);
      res.writeHead(400).end();
    }
    return;
  }

  if (method === 'GET' && url === '/stats') {
    const body = JSON.stringify({ OrderCount: orderCount, TotalRevenue: totalRevenue });
    res.writeHead(200, { 'Content-Type': 'application/json' }).end(body);
    return;
  }

  res.writeHead(404).end();
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`analytics-svc (node) listening on :${PORT}`);
});
