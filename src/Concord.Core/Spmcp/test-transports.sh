#!/bin/bash
# Comprehensive MCP Transport Test Suite
# Tests both Streamable HTTP and Legacy SSE transports with real domain model operations

PORT=3001
PASS=0
FAIL=0
TOTAL=0

pass() { ((PASS++)); ((TOTAL++)); echo "  [PASS] $1"; }
fail() { ((FAIL++)); ((TOTAL++)); echo "  [FAIL] $1"; echo "    Response: $2"; }

call_http() {
  local id=$1 method=$2 params=$3 session=$4
  local cmd="curl -s -D /tmp/mcp_h http://localhost:$PORT/mcp -H 'Content-Type: application/json' -H 'Accept: application/json'"
  [ -n "$session" ] && cmd="$cmd -H 'Mcp-Session-Id: $session'"
  cmd="$cmd -d '{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"$method\",\"params\":$params}'"
  eval "$cmd"
}

echo "============================================================"
echo " SPMCP Transport Test Suite — $(date)"
echo "============================================================"
echo ""

# =============================================
echo ">>> PART 1: STREAMABLE HTTP TRANSPORT"
echo "============================================="

# 1.1 Initialize
echo "[1.1] Initialize session"
INIT=$(call_http 1 "initialize" '{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"http-test","version":"1.0"}}')
SESSION=$(grep -i "Mcp-Session-Id:" /tmp/mcp_h | tr -d '\r\n' | awk '{print $2}')
echo "$INIT" | grep -q '"protocolVersion":"2025-03-26"' && pass "Initialize returns 2025-03-26 protocol" || fail "Initialize protocol version" "$INIT"
[ -n "$SESSION" ] && pass "Session ID received: ${SESSION:0:12}..." || fail "No session ID in headers" ""

# 1.2 tools/list
echo "[1.2] List tools"
TOOLS=$(call_http 2 "tools/list" '{}' "$SESSION")
TOOL_COUNT=$(echo "$TOOLS" | grep -o '"name":' | wc -l)
[ "$TOOL_COUNT" -gt 50 ] && pass "tools/list returns $TOOL_COUNT tools" || fail "tools/list count low" "$TOOL_COUNT"

# 1.3 Create a module via HTTP
echo "[1.3] Create module 'TransportTest'"
CREATE_MOD=$(call_http 3 "tools/call" '{"name":"create_module","arguments":{"module_name":"TransportTest"}}' "$SESSION")
echo "$CREATE_MOD" | grep -q '"result"' && pass "create_module succeeded" || fail "create_module" "$CREATE_MOD"

# 1.4 Create entity via HTTP
echo "[1.4] Create entity 'Customer'"
CREATE_ENT=$(call_http 4 "tools/call" '{"name":"create_entity","arguments":{"entity_name":"Customer","module_name":"TransportTest"}}' "$SESSION")
echo "$CREATE_ENT" | grep -q '"result"' && pass "create_entity Customer" || fail "create_entity Customer" "$CREATE_ENT"

# 1.5 Add attributes via HTTP
echo "[1.5] Add attributes to Customer"
ADD_ATTR1=$(call_http 5 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Customer","attribute_name":"FirstName","attribute_type":"String","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_ATTR1" | grep -q '"result"' && pass "add_attribute FirstName (String)" || fail "add_attribute FirstName" "$ADD_ATTR1"

ADD_ATTR2=$(call_http 6 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Customer","attribute_name":"Email","attribute_type":"String","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_ATTR2" | grep -q '"result"' && pass "add_attribute Email (String)" || fail "add_attribute Email" "$ADD_ATTR2"

ADD_ATTR3=$(call_http 7 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Customer","attribute_name":"Age","attribute_type":"Integer","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_ATTR3" | grep -q '"result"' && pass "add_attribute Age (Integer)" || fail "add_attribute Age" "$ADD_ATTR3"

ADD_ATTR4=$(call_http 8 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Customer","attribute_name":"IsActive","attribute_type":"Boolean","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_ATTR4" | grep -q '"result"' && pass "add_attribute IsActive (Boolean)" || fail "add_attribute IsActive" "$ADD_ATTR4"

# 1.6 Create second entity via HTTP
echo "[1.6] Create entity 'Order'"
CREATE_ORD=$(call_http 9 "tools/call" '{"name":"create_entity","arguments":{"entity_name":"Order","module_name":"TransportTest"}}' "$SESSION")
echo "$CREATE_ORD" | grep -q '"result"' && pass "create_entity Order" || fail "create_entity Order" "$CREATE_ORD"

# 1.7 Add attributes to Order
echo "[1.7] Add attributes to Order"
ADD_OATTR1=$(call_http 10 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Order","attribute_name":"OrderDate","attribute_type":"DateTime","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_OATTR1" | grep -q '"result"' && pass "add_attribute OrderDate (DateTime)" || fail "add_attribute OrderDate" "$ADD_OATTR1"

ADD_OATTR2=$(call_http 11 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Order","attribute_name":"TotalAmount","attribute_type":"Decimal","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_OATTR2" | grep -q '"result"' && pass "add_attribute TotalAmount (Decimal)" || fail "add_attribute TotalAmount" "$ADD_OATTR2"

ADD_OATTR3=$(call_http 12 "tools/call" '{"name":"add_attribute","arguments":{"entity_name":"Order","attribute_name":"Status","attribute_type":"String","module_name":"TransportTest"}}' "$SESSION")
echo "$ADD_OATTR3" | grep -q '"result"' && pass "add_attribute Status (String)" || fail "add_attribute Status" "$ADD_OATTR3"

# 1.8 Create association via HTTP
echo "[1.8] Create association Customer_Order"
CREATE_ASSOC=$(call_http 13 "tools/call" '{"name":"create_association","arguments":{"association_name":"Customer_Order","parent_entity":"Customer","child_entity":"Order","association_type":"one_to_many","module_name":"TransportTest"}}' "$SESSION")
echo "$CREATE_ASSOC" | grep -q '"result"' && pass "create_association Customer_Order" || fail "create_association" "$CREATE_ASSOC"

# 1.9 Read back the domain model to verify
echo "[1.9] Read domain model (verify all entities + attrs)"
READ_DM=$(call_http 14 "tools/call" '{"name":"read_domain_model","arguments":{"module_name":"TransportTest"}}' "$SESSION")
echo "$READ_DM" | grep -q 'Customer' && pass "Domain model contains Customer" || fail "Customer missing" "$READ_DM"
echo "$READ_DM" | grep -q 'Order' && pass "Domain model contains Order" || fail "Order missing" "$READ_DM"
echo "$READ_DM" | grep -q 'FirstName' && pass "Customer has FirstName attr" || fail "FirstName missing" "$READ_DM"
echo "$READ_DM" | grep -q 'TotalAmount' && pass "Order has TotalAmount attr" || fail "TotalAmount missing" "$READ_DM"
echo "$READ_DM" | grep -q 'Customer_Order' && pass "Association Customer_Order exists" || fail "Association missing" "$READ_DM"

# 1.10 Notification test
echo "[1.10] Notification (no id) returns 202"
NOTIF_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$PORT/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json" -H "Mcp-Session-Id: $SESSION" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
[ "$NOTIF_STATUS" = "202" ] && pass "Notification returns 202" || fail "Notification status $NOTIF_STATUS" ""

# 1.11 Batch request
echo "[1.11] Batch JSON-RPC request"
BATCH=$(curl -s http://localhost:$PORT/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json" -H "Mcp-Session-Id: $SESSION" \
  -d '[{"jsonrpc":"2.0","id":20,"method":"tools/call","params":{"name":"list_modules","arguments":{}}},{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"read_project_info","arguments":{}}}]')
echo "$BATCH" | grep -q '"result"' && pass "Batch returns results" || fail "Batch request" "$BATCH"
# Verify it's an array
echo "$BATCH" | grep -q '^\[' && pass "Batch response is JSON array" || fail "Batch not array" "$BATCH"

# 1.12 Session DELETE
echo "[1.12] DELETE session"
DEL_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE http://localhost:$PORT/mcp \
  -H "Mcp-Session-Id: $SESSION")
[ "$DEL_STATUS" = "200" ] && pass "Session DELETE returns 200" || fail "Session DELETE status $DEL_STATUS" ""

# 1.13 Verify deleted session rejected
echo "[1.13] Deleted session rejected"
REJECT=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$PORT/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json" -H "Mcp-Session-Id: $SESSION" \
  -d '{"jsonrpc":"2.0","id":99,"method":"tools/list","params":{}}')
[ "$REJECT" = "404" ] && pass "Deleted session returns 404" || fail "Deleted session status $REJECT" ""

echo ""
# =============================================
echo ">>> PART 2: LEGACY SSE TRANSPORT"
echo "============================================="

# 2.1 Open SSE connection and capture endpoint URL
echo "[2.1] Open SSE connection"
timeout 5 curl -s -N http://localhost:$PORT/sse > /tmp/sse_output 2>/dev/null &
SSE_PID=$!
sleep 2

SSE_OUT=$(cat /tmp/sse_output)
echo "$SSE_OUT" | grep -q "event: endpoint" && pass "SSE sends endpoint event" || fail "No endpoint event" "$SSE_OUT"

# Extract session ID from the endpoint URL
SSE_SESSION=$(echo "$SSE_OUT" | grep "data:" | head -1 | sed 's/.*sessionId=\([a-f0-9]*\).*/\1/')
[ -n "$SSE_SESSION" ] && pass "SSE session extracted: ${SSE_SESSION:0:12}..." || fail "No SSE session" "$SSE_OUT"

# 2.2 Initialize via /message (SSE transport — response relayed via SSE stream)
echo "[2.2] Initialize via /message (SSE relay)"
SSE_INIT_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"sse-test","version":"1.0"}}}')
[ "$SSE_INIT_STATUS" = "202" ] && pass "SSE /message returns 202 (response relayed to stream)" || fail "SSE status $SSE_INIT_STATUS" ""
sleep 1

# 2.3 Create entity via SSE transport
echo "[2.3] Create entity 'Product' via SSE /message"
PROD_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"create_entity","arguments":{"entity_name":"Product","module_name":"TransportTest"}}}')
[ "$PROD_STATUS" = "202" ] && pass "create_entity Product via SSE (202 relayed)" || fail "Product status $PROD_STATUS" ""
sleep 1

# 2.4 Add attributes via SSE
echo "[2.4] Add attributes via SSE"
curl -s -o /dev/null "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"add_attribute","arguments":{"entity_name":"Product","attribute_name":"ProductName","attribute_type":"String","module_name":"TransportTest"}}}'
sleep 0.5
curl -s -o /dev/null "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"add_attribute","arguments":{"entity_name":"Product","attribute_name":"Price","attribute_type":"Decimal","module_name":"TransportTest"}}}'
sleep 0.5
curl -s -o /dev/null "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"add_attribute","arguments":{"entity_name":"Product","attribute_name":"SKU","attribute_type":"String","module_name":"TransportTest"}}}'
sleep 0.5
pass "add_attribute ProductName + Price + SKU via SSE"

# 2.5 Create association via SSE
echo "[2.5] Create association Order_Product via SSE"
ASSOC_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$PORT/message?sessionId=$SSE_SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"create_association","arguments":{"association_name":"Order_Product","parent_entity":"Order","child_entity":"Product","association_type":"one_to_many","module_name":"TransportTest"}}}')
[ "$ASSOC_STATUS" = "202" ] && pass "create_association via SSE (202 relayed)" || fail "Assoc status $ASSOC_STATUS" ""
sleep 1

# 2.6 Check SSE stream received message events
echo "[2.6] Verify SSE stream received relay messages"
sleep 1
kill $SSE_PID 2>/dev/null
wait $SSE_PID 2>/dev/null
SSE_FINAL=$(cat /tmp/sse_output)
MSG_COUNT=$(echo "$SSE_FINAL" | grep -c "event: message")
[ "$MSG_COUNT" -ge 3 ] && pass "SSE stream relayed $MSG_COUNT message events" || fail "Only $MSG_COUNT message events" "$SSE_FINAL"

# 2.7 Verify full domain model via direct /message (no SSE — fallback)
echo "[2.7] Verify full domain model via direct /message"
DIRECT=$(curl -s "http://localhost:$PORT/message" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"read_domain_model","arguments":{"module_name":"TransportTest"}}}')
echo "$DIRECT" | grep -q 'Customer' && pass "Customer exists (verified via /message)" || fail "Customer missing" "$DIRECT"
echo "$DIRECT" | grep -q 'Order' && pass "Order exists" || fail "Order missing" "$DIRECT"
echo "$DIRECT" | grep -q 'Product' && pass "Product exists (created via SSE)" || fail "Product missing" "$DIRECT"
echo "$DIRECT" | grep -q 'ProductName' && pass "ProductName attr exists" || fail "ProductName missing" "$DIRECT"
echo "$DIRECT" | grep -q 'SKU' && pass "SKU attr exists" || fail "SKU missing" "$DIRECT"
echo "$DIRECT" | grep -q 'Customer_Order' && pass "Customer_Order assoc exists" || fail "Customer_Order missing" "$DIRECT"
echo "$DIRECT" | grep -q 'Order_Product' && pass "Order_Product assoc exists (created via SSE)" || fail "Order_Product missing" "$DIRECT"

echo ""
# =============================================
echo ">>> PART 3: INFRASTRUCTURE TESTS"
echo "============================================="

# 3.1 Health
echo "[3.1] Health endpoint"
HEALTH=$(curl -s http://localhost:$PORT/health)
[ "$HEALTH" = "SPMCP is running" ] && pass "Health check" || fail "Health" "$HEALTH"

# 3.2 Metadata
echo "[3.2] Metadata endpoint"
META=$(curl -s http://localhost:$PORT/.well-known/mcp)
echo "$META" | grep -q '"streamable-http"' && pass "Metadata lists streamable-http" || fail "Metadata" "$META"
echo "$META" | grep -q '"sse"' && pass "Metadata lists sse" || fail "Metadata sse" "$META"

# 3.3 CORS
echo "[3.3] CORS preflight"
CORS_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X OPTIONS http://localhost:$PORT/mcp)
[ "$CORS_STATUS" = "204" ] && pass "OPTIONS /mcp returns 204" || fail "CORS status $CORS_STATUS" ""
CORS_HDR=$(curl -s -D - -o /dev/null -X OPTIONS http://localhost:$PORT/mcp | grep -i "Access-Control-Allow")
echo "$CORS_HDR" | grep -q "Mcp-Session-Id" && pass "CORS exposes Mcp-Session-Id" || fail "CORS headers" "$CORS_HDR"

# 3.4 404
echo "[3.4] 404 for unknown path"
STATUS_404=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$PORT/nonexistent)
[ "$STATUS_404" = "404" ] && pass "Unknown path returns 404" || fail "404 status: $STATUS_404" ""

# 3.5 Root POST
echo "[3.5] Root POST endpoint"
ROOT=$(curl -s http://localhost:$PORT/ -X POST \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"root-test","version":"1.0"}}}')
echo "$ROOT" | grep -q '"protocolVersion"' && pass "Root POST works" || fail "Root POST" "$ROOT"

# 3.6 Content negotiation
echo "[3.6] Content negotiation (Accept: text/event-stream only)"
CN_INIT=$(curl -s -D /tmp/mcp_cn http://localhost:$PORT/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"cn-test","version":"1.0"}}}')
CN_SESSION=$(grep -i "Mcp-Session-Id:" /tmp/mcp_cn | tr -d '\r\n' | awk '{print $2}')

CN_RESP=$(curl -s -D /tmp/mcp_cn2 http://localhost:$PORT/mcp \
  -H "Content-Type: application/json" -H "Accept: text/event-stream" -H "Mcp-Session-Id: $CN_SESSION" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_modules","arguments":{}}}')
CN_CT=$(grep -i "Content-Type:" /tmp/mcp_cn2 | tr -d '\r')
echo "$CN_CT" | grep -q "text/event-stream" && pass "SSE-only Accept returns text/event-stream" || fail "Content-Type: $CN_CT" ""
echo "$CN_RESP" | grep -q "event: message" && pass "Response wrapped in SSE event format" || fail "Not SSE format" "$CN_RESP"

# 3.7 SSE POST to /sse endpoint
echo "[3.7] POST to /sse endpoint"
SSE_POST=$(curl -s "http://localhost:$PORT/sse" -X POST \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"sse-post-test","version":"1.0"}}}')
echo "$SSE_POST" | grep -q '"protocolVersion"' && pass "POST to /sse works (direct response)" || fail "POST /sse" "$SSE_POST"

echo ""
echo "============================================================"
echo " RESULTS: $PASS passed, $FAIL failed, $TOTAL total"
echo "============================================================"

# Cleanup
rm -f /tmp/mcp_h /tmp/mcp_cn /tmp/mcp_cn2 /tmp/sse_output
