<?php
/**
 * _common.php  —  SecureChannel shared internals
 * ============================================================================
 * Included by every endpoint file.  Never accessed directly (blocked by
 * .htaccess).  Contains the database connection, all helper functions, and
 * the cryptographic verification logic.
 *
 * CONFIGURATION  —  edit DB_* constants before deployment
 * ============================================================================
 */

declare(strict_types=1);

require 'config.php'
const DB_DSN  = 'mysql:host='.DB_HOST.';dbname='.DB_NAME.';charset=utf8mb4';
// ── Limits ───────────────────────────────────────────────────────────────────
const MAX_DATA_BYTES       = 87380;   // max encrypted payload (~64 KB plaintext in Base64)
const MAX_RESULTS_PER_PAGE = 200;     // max messages returned per GET

// ── Universal response headers (replaces mod_headers) ────────────────────────
header('Content-Type: application/json; charset=utf-8');
header('X-Content-Type-Options: nosniff');
header('X-Frame-Options: DENY');
header('Referrer-Policy: no-referrer');
header('Cache-Control: no-store, no-cache, must-revalidate');

// ════════════════════════════════════════════════════════════════════════════
// DATABASE
// ════════════════════════════════════════════════════════════════════════════

/**
 * Returns a singleton PDO instance, created lazily on first call.
 */
function getDB(): PDO
{
    static $pdo = null;
    if ($pdo === null) {
        $pdo = new PDO(DB_DSN, DB_USER, DB_PASS, [
            PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES   => false,
        ]);
    }
    return $pdo;
}

/**
 * Checks whether a (channel_id, auth_token) pair exists in the channels table.
 * Leverages the unique index on channel_id for an O(1) lookup.
 */
function channelCredentialsValid(PDO $db, string $channelId, string $authToken): bool
{
    $stmt = $db->prepare(
        'SELECT 1 FROM channels WHERE channel_id = :cid AND auth_token = :tok LIMIT 1'
    );
    $stmt->execute([':cid' => $channelId, ':tok' => $authToken]);
    return $stmt->fetch() !== false;
}

// ════════════════════════════════════════════════════════════════════════════
// CRYPTOGRAPHIC VERIFICATION
// ════════════════════════════════════════════════════════════════════════════

/**
 * Verifies internal consistency of the submitted credentials without ever
 * seeing the original password.
 *
 * Protocol:
 *   channel_id = Hex( SHA-256(password) )
 *   auth_token = Hex( SHA-256( hex2bin(channel_id) ) )
 *
 * This function checks:  SHA-256( hex2bin(channel_id) ) === auth_token
 * using hash_equals() for constant-time comparison (prevents timing attacks).
 */
function verifyAuthToken(string $channelId, string $authToken): bool
{
    $channelBytes = hex2bin($channelId);
    if ($channelBytes === false) return false;

    $expected = hash('sha256', $channelBytes, false);  // lowercase hex
    return hash_equals($expected, strtolower($authToken));
}

// ════════════════════════════════════════════════════════════════════════════
// INPUT VALIDATION
// ════════════════════════════════════════════════════════════════════════════

/**
 * Reads and JSON-decodes the raw request body.
 * Sends HTTP 400 and exits on any failure.
 *
 * @return array<string, mixed>
 */
function readJsonBody(): array
{
    $raw = file_get_contents('php://input');
    if ($raw === false || trim($raw) === '') {
        jsonResponse(['error' => 'Empty request body'], 400);
    }
    try {
        $data = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
    } catch (\JsonException $e) {
        jsonResponse(['error' => 'Malformed JSON: ' . $e->getMessage()], 400);
    }
    if (!is_array($data)) {
        jsonResponse(['error' => 'Request body must be a JSON object'], 400);
    }
    return $data;
}

/**
 * Extracts and validates a 64-character lowercase hex string from a POST body.
 * Sends 400 and exits if the field is absent, not a string, or not valid hex.
 *
 * @param array<string, mixed> $body
 */
function requireHex64(array $body, string $field): string
{
    if (!isset($body[$field]) || !is_string($body[$field])) {
        jsonResponse(['error' => "Missing or invalid field: $field"], 400);
    }
    $val = strtolower(trim($body[$field]));
    if (!preg_match('/^[0-9a-f]{64}$/', $val)) {
        jsonResponse(['error' => "Field '$field' must be a 64-character lowercase hex string"], 400);
    }
    return $val;
}

/**
 * Extracts and validates a 64-character lowercase hex string from the query string.
 * Sends 400 and exits on failure.
 */
function requireHexParam(string $param): string
{
    if (!isset($_GET[$param]) || !is_string($_GET[$param])) {
        jsonResponse(['error' => "Missing query parameter: $param"], 400);
    }
    $val = strtolower(trim($_GET[$param]));
    if (!preg_match('/^[0-9a-f]{64}$/', $val)) {
        jsonResponse(['error' => "Parameter '$param' must be a 64-character lowercase hex string"], 400);
    }
    return $val;
}

// ════════════════════════════════════════════════════════════════════════════
// RESPONSE
// ════════════════════════════════════════════════════════════════════════════

/**
 * Encodes $data as JSON, sets the HTTP status code, and exits.
 * This function never returns.
 *
 * @param array<mixed> $data
 */
function jsonResponse(array $data, int $status = 200): never
{
    http_response_code($status);
    echo json_encode($data,
        JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_NUMERIC_CHECK);
    exit;
}

/**
 * Rejects any HTTP method other than the ones listed.
 * Sends 405 Method Not Allowed and exits.
 */
function requireMethod(string ...$allowed): void
{
    $method = strtoupper($_SERVER['REQUEST_METHOD'] ?? '');
    if (!in_array($method, $allowed, true)) {
        header('Allow: ' . implode(', ', $allowed));
        jsonResponse(['error' => 'Method not allowed'], 405);
    }
}
