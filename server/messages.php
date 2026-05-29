<?php
/**
 * messages.php
 * ============================================================================
 * Endpoint: POST /messages.php  — send an encrypted message
 *           GET  /messages.php  — retrieve new messages since a cursor ID
 *
 * ── POST ────────────────────────────────────────────────────────────────────
 * Request body (JSON):
 *   {
 *     "channel_id": "<64-char hex>",
 *     "auth_token":  "<64-char hex>",
 *     "data":        "<Base64-encoded AES-256-CBC ciphertext>"
 *   }
 *
 * The server stores "data" verbatim and never attempts to decode it.
 *
 * Responses:
 *   201  { "status": "ok", "id": <int> }
 *   400  missing/malformed fields, or payload too large
 *   401  invalid credentials
 *   405  wrong HTTP method
 *
 * ── GET ─────────────────────────────────────────────────────────────────────
 * Query parameters:
 *   channel_id  <64-char hex>
 *   auth_token  <64-char hex>
 *   since_id    <int>   — return only messages with id > this value (default 0)
 *
 * Responses:
 *   200  {
 *          "messages": [ { "id": <int>, "data": "<base64>", "created_at": "<datetime>" }, ... ],
 *          "last_id":  <int>,
 *          "count":    <int>
 *        }
 *   400  missing/malformed parameters
 *   401  invalid credentials
 * ============================================================================
 */

declare(strict_types=1);
require_once __DIR__ . '/_common.php';

requireMethod('GET', 'POST');

$method = strtoupper($_SERVER['REQUEST_METHOD']);

try {
    if ($method === 'POST') {
        handlePost();
    } else {
        handleGet();
    }
} catch (PDOException $e) {
    error_log('[SecureChannel] messages.php DB error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
} catch (Throwable $e) {
    error_log('[SecureChannel] messages.php error: ' . $e->getMessage());
    jsonResponse(['error' => 'Internal server error'], 500);
}

// ════════════════════════════════════════════════════════════════════════════

function handlePost(): never
{
    $body      = readJsonBody();
    $channelId = requireHex64($body, 'channel_id');
    $authToken = requireHex64($body, 'auth_token');

    // Validate encrypted payload
    if (empty($body['data']) || !is_string($body['data'])) {
        jsonResponse(['error' => 'Missing field: data'], 400);
    }
    $data = $body['data'];
    if (strlen($data) > MAX_DATA_BYTES) {
        jsonResponse(['error' => 'Payload exceeds maximum allowed size'], 400);
    }
    // Reject obvious non-Base64 content early (saves a DB round-trip)
    if (!preg_match('/^[A-Za-z0-9+\/=]+$/', $data)) {
        jsonResponse(['error' => 'Field "data" must be Base64-encoded ciphertext'], 400);
    }

    if (!verifyAuthToken($channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    $db = getDB();

    if (!channelCredentialsValid($db, $channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    $stmt = $db->prepare(
        'INSERT INTO messages (channel_id, encrypted_data, created_at)
         VALUES (:cid, :data, UTC_TIMESTAMP())'
    );
    $stmt->execute([':cid' => $channelId, ':data' => $data]);
    $newId = (int)$db->lastInsertId();

    jsonResponse(['status' => 'ok', 'id' => $newId], 201);
}

// ────────────────────────────────────────────────────────────────────────────

function handleGet(): never
{
    $channelId = requireHexParam('channel_id');
    $authToken = requireHexParam('auth_token');
    $sinceId   = max(0, (int)($_GET['since_id'] ?? 0));

    if (!verifyAuthToken($channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    $db = getDB();

    if (!channelCredentialsValid($db, $channelId, $authToken)) {
        jsonResponse(['error' => 'Invalid credentials'], 401);
    }

    // This query is fully covered by the composite index idx_channel_since (channel_id, id),
    // making incremental polling O(log n + k) regardless of total message volume.
    $stmt = $db->prepare(
        'SELECT id, encrypted_data AS data, created_at
         FROM   messages
         WHERE  channel_id = :cid
           AND  id > :sid
         ORDER  BY id ASC
         LIMIT  :lim'
    );
    $stmt->bindValue(':cid', $channelId,           PDO::PARAM_STR);
    $stmt->bindValue(':sid', $sinceId,             PDO::PARAM_INT);
    $stmt->bindValue(':lim', MAX_RESULTS_PER_PAGE, PDO::PARAM_INT);
    $stmt->execute();

    $messages = $stmt->fetchAll(PDO::FETCH_ASSOC);

    // PDO returns numeric columns as strings; cast for clean JSON output
    foreach ($messages as &$msg) {
        $msg['id'] = (int)$msg['id'];
    }
    unset($msg);

    $lastId = empty($messages) ? $sinceId : (int)end($messages)['id'];

    jsonResponse([
        'messages' => $messages,
        'last_id'  => $lastId,
        'count'    => count($messages),
    ], 200);
}
