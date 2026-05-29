<?php
require $_SERVER['DOCUMENT_ROOT'].'/assets/setup/env.php';

$uri = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
$uri = explode( '/', $uri );

if (isset($uri[3])) {
	if ($uri[3] == "version") {
		GetCurrentClientVersion();
	}
	if ($uri[3] == "minversion") {
		GetSupportedClientVersion();
	}
} else {
	//header("HTTP/1.1 404 Not Found");
	//exit();
	var_dump($uri);
}

function sendOutput($data, $httpHeaders=array()) {
	header_remove('Set-Cookie');

	if (is_array($httpHeaders) && count($httpHeaders)) {
		foreach ($httpHeaders as $httpHeader) {
			header($httpHeader);
		}
	}

	echo $data;
}
	
function GetCurrentClientVersion() {
	$responseData = json_encode(WURMSHARE_CURRENT_VERSION);
	sendOutput($responseData, array('Content-Type: application/json', 'HTTP/1.1 200 OK'));
}

function GetSupportedClientVersion() {
	$responseData = json_encode(WURMSHARE_MINIMUM_VERSION);
	sendOutput($responseData, array('Content-Type: application/json', 'HTTP/1.1 200 OK'));
}
Exit(0);