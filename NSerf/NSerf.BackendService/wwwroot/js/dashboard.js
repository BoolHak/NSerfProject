// NSerf Dashboard Logic - Pure p5.js Implementation
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/serfHub")
    .withAutomaticReconnect()
    .build();

// --- State ---
let localNodeName = "";
let nodesData = {}; // { name: { x, y, color, address, tags } }
const particles = [];

// Node colors
const COLORS = {
    localNode: [59, 130, 246],      // Blue
    remoteNode: [16, 185, 129],     // Green
    gossip: [255, 255, 0],          // Yellow
    probe: [239, 68, 68],           // Red
    pushpull: [16, 185, 129],       // Green
    packet: [168, 85, 247]          // Purple
};

// --- Particle Class ---
class P5Particle {
    constructor(startX, startY, endX, endY, color) {
        this.startX = startX;
        this.startY = startY;
        this.endX = endX;
        this.endY = endY;
        this.color = color; // [r, g, b]
        this.progress = 0;
        this.speed = 0.015 + Math.random() * 0.01; // 1-1.5 seconds travel time
    }

    update() {
        this.progress += this.speed;
        return this.progress < 1;
    }

    getX() {
        return this.startX + (this.endX - this.startX) * this.progress;
    }

    getY() {
        return this.startY + (this.endY - this.startY) * this.progress;
    }

    draw(p) {
        const x = this.getX();
        const y = this.getY();
        const [r, g, b] = this.color;

        // Outer glow (smaller)
        p.fill(r, g, b, 60);
        p.ellipse(x, y, 15, 15);

        // Main particle (smaller)
        p.fill(r, g, b, 200);
        p.ellipse(x, y, 8, 8);

        // Bright core (smaller)
        p.fill(255, 255, 255, 230);
        p.ellipse(x, y, 3, 3);
    }
}

// --- p5.js Sketch ---
const sketch = (p) => {
    const NODE_SIZE = 20; // Smaller for 20 nodes
    const EDGE_COLOR = [100, 116, 139, 50]; // Slate with more transparency

    p.setup = function () {
        const canvas = p.createCanvas(p.windowWidth, p.windowHeight);
        canvas.parent('network-graph');
        p.textFont('Arial');
        p.noStroke();
    };

    p.windowResized = function () {
        p.resizeCanvas(p.windowWidth, p.windowHeight);
        // Reposition nodes when window resizes
        repositionNodes();
    };

    p.draw = function () {
        // Dark background
        p.background(15, 23, 42);

        // Draw edges first (behind nodes)
        drawEdges(p);

        // Draw nodes
        drawNodes(p);

        // Update and draw particles (on top)
        drawParticles(p);

        // Debug info
        p.fill(255, 255, 0);
        p.textSize(14);
        p.text(`Particles: ${particles.length}`, 15, 25);
        p.text(`Nodes: ${Object.keys(nodesData).length}`, 15, 45);
    };

    function drawEdges(p) {
        if (!localNodeName || !nodesData[localNodeName]) return;

        const localNode = nodesData[localNodeName];
        p.stroke(...EDGE_COLOR);
        p.strokeWeight(2);

        Object.keys(nodesData).forEach(name => {
            if (name !== localNodeName) {
                const node = nodesData[name];
                p.line(localNode.x, localNode.y, node.x, node.y);
            }
        });

        p.noStroke();
    }

    function drawNodes(p) {
        Object.keys(nodesData).forEach(name => {
            const node = nodesData[name];
            const isLocal = name === localNodeName;
            const color = isLocal ? COLORS.localNode : COLORS.remoteNode;

            // Outer glow
            p.fill(color[0], color[1], color[2], 40);
            p.ellipse(node.x, node.y, NODE_SIZE + 20, NODE_SIZE + 20);

            // Main node
            p.fill(...color);
            p.ellipse(node.x, node.y, NODE_SIZE, NODE_SIZE);

            // White border
            p.stroke(255);
            p.strokeWeight(2);
            p.noFill();
            p.ellipse(node.x, node.y, NODE_SIZE, NODE_SIZE);
            p.noStroke();

            // Node label (smaller for more nodes)
            p.fill(255);
            p.textAlign(p.CENTER, p.TOP);
            p.textSize(10);
            p.text(name, node.x, node.y + NODE_SIZE / 2 + 5);
        });
    }

    function drawParticles(p) {
        for (let i = particles.length - 1; i >= 0; i--) {
            const particle = particles[i];
            if (!particle.update()) {
                particles.splice(i, 1);
                continue;
            }
            particle.draw(p);
        }
    }
};

function repositionNodes() {
    const nodeNames = Object.keys(nodesData);
    const count = nodeNames.length;
    if (count === 0) return;

    const centerX = window.innerWidth / 2;
    const centerY = window.innerHeight / 2;
    // Larger radius to accommodate more nodes
    const radius = Math.min(window.innerWidth, window.innerHeight) * 0.35;

    nodeNames.forEach((name, i) => {
        if (name === localNodeName) {
            // Local node in center
            nodesData[name].x = centerX;
            nodesData[name].y = centerY;
        } else {
            // Distribute others in a circle
            const angle = (2 * Math.PI / (count - 1 || 1)) * i;
            nodesData[name].x = centerX + radius * Math.cos(angle);
            nodesData[name].y = centerY + radius * Math.sin(angle);
        }
    });
}

// Initialize p5
new p5(sketch);

// --- API Polling ---
async function refreshMembers() {
    try {
        const response = await fetch('/api/cluster');
        if (!response.ok) return;

        const data = await response.json();

        // Update stats
        document.getElementById('stat-members').innerText = data.totalMembers;
        document.getElementById('stat-local').innerText = data.self;
        localNodeName = data.self;

        const currentNames = new Set(Object.keys(nodesData));
        const newNames = new Set();

        // Add/update nodes
        data.members.forEach(m => {
            newNames.add(m.name);

            if (!nodesData[m.name]) {
                // New node - add with temporary position
                nodesData[m.name] = {
                    x: window.innerWidth / 2 + (Math.random() - 0.5) * 200,
                    y: window.innerHeight / 2 + (Math.random() - 0.5) * 200,
                    address: m.address,
                    tags: m.tags
                };
                logEvent(`Member Joined: ${m.name}`, 'text-green-400');
            } else {
                // Update existing node data
                nodesData[m.name].address = m.address;
                nodesData[m.name].tags = m.tags;
            }
        });

        // Remove old nodes
        currentNames.forEach(name => {
            if (!newNames.has(name)) {
                delete nodesData[name];
                logEvent(`Member Left: ${name}`, 'text-red-400');
            }
        });

        // Reposition nodes in a nice layout
        repositionNodes();

    } catch (err) {
        console.error("Failed to refresh members", err);
    }
}

// --- Particle Spawning ---
function spawnParticle(fromNode, toNode, colorArray) {
    if (!nodesData[fromNode] || !nodesData[toNode]) return;

    const start = nodesData[fromNode];
    const end = nodesData[toNode];

    particles.push(new P5Particle(start.x, start.y, end.x, end.y, colorArray));
}

// Gossip round picks only 3-4 random nodes (like the real algorithm)
function spawnGossipRound(sourceNodeId, colorArray, count = 4) {
    const source = nodesData[sourceNodeId] ? sourceNodeId : localNodeName;
    if (!source || !nodesData[source]) return;

    const targets = Object.keys(nodesData).filter(n => n !== source);
    if (targets.length === 0) return;

    // Shuffle and pick 'count' random targets (or all if fewer)
    const shuffled = targets.sort(() => Math.random() - 0.5);
    const selected = shuffled.slice(0, Math.min(count, targets.length));

    selected.forEach(target => {
        spawnParticle(source, target, colorArray);
    });
}

// True broadcast (for user events)
function spawnBroadcast(sourceNodeId, colorArray) {
    const source = nodesData[sourceNodeId] ? sourceNodeId : localNodeName;
    if (!source || !nodesData[source]) return;

    Object.keys(nodesData).forEach(target => {
        if (target !== source) {
            spawnParticle(source, target, colorArray);
        }
    });
}

function spawnGossip() {
    if (!localNodeName) return;

    const targets = Object.keys(nodesData).filter(n => n !== localNodeName);
    if (targets.length === 0) return;

    const target = targets[Math.floor(Math.random() * targets.length)];
    spawnParticle(localNodeName, target, COLORS.gossip);
}

// --- Actions ---
function showModal(id) {
    document.getElementById(id).classList.remove('hidden');
}

function hideModal(id) {
    document.getElementById(id).classList.add('hidden');
}

async function submitEvent() {
    const name = document.getElementById('evt-name').value;
    const payload = document.getElementById('evt-payload').value;
    const coalesce = document.getElementById('evt-coalesce').checked;

    if (!name) return;

    try {
        await connection.invoke("SendUserEvent", name, payload, coalesce);
        hideModal('send-event-modal');
        document.getElementById('evt-name').value = '';
        spawnBroadcast(localNodeName, COLORS.localNode);
    } catch (err) {
        console.error(err);
        alert("Failed to send event");
    }
}

async function submitQuery() {
    const name = document.getElementById('qry-name').value;
    const payload = document.getElementById('qry-payload').value;

    if (!name) return;

    try {
        await connection.invoke("SendQuery", name, payload);
        hideModal('send-query-modal');
        document.getElementById('qry-name').value = '';
        spawnBroadcast(localNodeName, COLORS.localNode);
    } catch (err) {
        console.error(err);
        alert("Failed to send query");
    }
}

function logEvent(msg, colorClass = 'text-gray-300') {
    const log = document.getElementById('event-log');
    const entry = document.createElement('div');
    const time = new Date().toLocaleTimeString();
    entry.className = `${colorClass}`;
    entry.innerText = `[${time}] ${msg}`;
    log.prepend(entry);

    // Keep log manageable
    while (log.children.length > 50) {
        log.removeChild(log.lastChild);
    }
}

// --- SignalR Events ---
connection.on("EventSent", (data) => {
    let color = 'text-blue-400';
    if (data.event === 'user' || data.event === 'query') {
        spawnBroadcast(localNodeName, COLORS.localNode);
    } else {
        color = 'text-yellow-400';
        spawnGossip();
    }
    logEvent(`${data.event.toUpperCase()}: ${data.name} ${data.payload ? '(' + data.payload + ')' : ''}`, color);
});

// Real-time Network Traffic Visualization from Logs
connection.on("NetworkTraffic", (data) => {
    if (!localNodeName) return;

    switch (data.type) {
        case 'gossip':
            // Now we get the actual target node from the log
            logEvent(`GOSSIP -> ${data.target}`, 'text-yellow-200');
            if (nodesData[data.target]) {
                spawnParticle(localNodeName, data.target, COLORS.gossip);
            }
            break;

        case 'probe':
            logEvent(`PROBE -> ${data.target}`, 'text-red-400');
            // Try to spawn to specific target, or broadcast if unknown
            if (nodesData[data.target]) {
                spawnParticle(localNodeName, data.target, COLORS.probe);
            } else {
                spawnGossip();
            }
            break;

        case 'pushpull':
            logEvent(`SYNC -> ${data.target}`, 'text-green-400');
            if (nodesData[data.target]) {
                spawnParticle(localNodeName, data.target, COLORS.pushpull);
            } else {
                spawnGossip();
            }
            break;

        case 'packet':
            logEvent(`PACKET <- ${data.target} (${data.bytes}b)`, 'text-purple-400');
            // Incoming packet - spawn from random node to local
            const senders = Object.keys(nodesData).filter(n => n !== localNodeName);
            if (senders.length > 0) {
                const sender = senders[Math.floor(Math.random() * senders.length)];
                spawnParticle(sender, localNodeName, COLORS.packet);
            }
            break;

        default:
            console.warn("[NetworkTraffic] Unknown type:", data.type);
    }
});

// --- Init ---
connection.start().then(() => {
    document.getElementById('connection-status').classList.remove('status-disconnected');
    document.getElementById('connection-status').classList.add('status-connected');
    document.getElementById('connection-text').innerText = "Connected";

    // Start polling for cluster membership
    refreshMembers();
    setInterval(refreshMembers, 2000);

    // No ambient gossip - visualization is now 100% driven by real log events
}).catch(err => console.error(err));
