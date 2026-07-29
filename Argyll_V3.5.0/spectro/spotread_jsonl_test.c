/*
 * Tests for IwashiScope machine-readable spotread output.
 * Copyright (C) 2026 Yamonov
 *
 * This file is free software: you can redistribute it and/or modify it
 * under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, version 3 of the License.
 *
 * This file is distributed without any warranty; see ../../LICENSE and
 * ../../NOTICE for details.
 */

#include <stdio.h>
#include <string.h>

/*
 * Include the implementation so this test exercises the same private UTF-8
 * path used by every JSON string without exposing a test-only production API.
 */
#include "spotread_jsonl.c"

static void print_bytes(const unsigned char *value, size_t length) {
	size_t i;

	for (i = 0; i < length; i++)
		fprintf(stderr, "%s%02x", i == 0 ? "" : " ", value[i]);
}

static int expect_json_string(
	const char *name,
	const unsigned char *input,
	const unsigned char *expected,
	size_t expected_length
) {
	json_writer writer;
	const unsigned char *actual = NULL;
	size_t actual_length = 0;
	int failed = 0;

	writer.generator = yajl_gen_alloc(NULL);
	writer.failed = writer.generator == NULL;
	if (!writer.failed
	 && !yajl_gen_config(writer.generator, yajl_gen_validate_utf8, 1))
		writer.failed = 1;

	json_string(&writer, (const char *)input);
	if (writer.failed
	 || yajl_gen_get_buf(
		writer.generator,
		&actual,
		&actual_length
	 ) != yajl_gen_status_ok) {
		fprintf(stderr, "%s: JSON generation failed\n", name);
		failed = 1;
	} else if (actual_length != expected_length
	        || memcmp(actual, expected, expected_length) != 0) {
		fprintf(stderr, "%s: unexpected JSON bytes\nexpected: ", name);
		print_bytes(expected, expected_length);
		fprintf(stderr, "\nactual:   ");
		print_bytes(actual, actual_length);
		fputc('\n', stderr);
		failed = 1;
	}

	if (writer.generator != NULL)
		yajl_gen_free(writer.generator);
	return failed;
}

int main(void) {
	static const unsigned char valid_utf8[] = {
		'A', 0xc2, 0xa2, 0xe2, 0x82, 0xac,
		0xf0, 0x9f, 0x98, 0x80, 'Z', 0
	};
	static const unsigned char valid_utf8_json[] = {
		'"', 'A', 0xc2, 0xa2, 0xe2, 0x82, 0xac,
		0xf0, 0x9f, 0x98, 0x80, 'Z', '"'
	};
	static const unsigned char invalid_byte[] = {
		'A', 0xff, 'Z', 0
	};
	static const unsigned char invalid_byte_json[] = {
		'"', 'A', 0xef, 0xbf, 0xbd, 'Z', '"'
	};
	static const unsigned char overlong[] = {
		0xc0, 0xaf, 0
	};
	static const unsigned char overlong_json[] = {
		'"', 0xef, 0xbf, 0xbd, 0xef, 0xbf, 0xbd, '"'
	};
	static const unsigned char surrogate[] = {
		0xed, 0xa0, 0x80, 0
	};
	static const unsigned char surrogate_json[] = {
		'"',
		0xef, 0xbf, 0xbd,
		0xef, 0xbf, 0xbd,
		0xef, 0xbf, 0xbd,
		'"'
	};
	static const unsigned char truncated[] = {
		0xe2, 0x82, 0
	};
	static const unsigned char truncated_json[] = {
		'"', 0xef, 0xbf, 0xbd, 0xef, 0xbf, 0xbd, '"'
	};
	static const unsigned char above_unicode_max[] = {
		0xf4, 0x90, 0x80, 0x80, 0
	};
	static const unsigned char above_unicode_max_json[] = {
		'"',
		0xef, 0xbf, 0xbd,
		0xef, 0xbf, 0xbd,
		0xef, 0xbf, 0xbd,
		0xef, 0xbf, 0xbd,
		'"'
	};
	int failed = 0;

	failed |= expect_json_string(
		"valid UTF-8",
		valid_utf8,
		valid_utf8_json,
		sizeof(valid_utf8_json)
	);
	failed |= expect_json_string(
		"invalid byte",
		invalid_byte,
		invalid_byte_json,
		sizeof(invalid_byte_json)
	);
	failed |= expect_json_string(
		"overlong encoding",
		overlong,
		overlong_json,
		sizeof(overlong_json)
	);
	failed |= expect_json_string(
		"surrogate",
		surrogate,
		surrogate_json,
		sizeof(surrogate_json)
	);
	failed |= expect_json_string(
		"truncated sequence",
		truncated,
		truncated_json,
		sizeof(truncated_json)
	);
	failed |= expect_json_string(
		"code point above Unicode maximum",
		above_unicode_max,
		above_unicode_max_json,
		sizeof(above_unicode_max_json)
	);

	if (failed)
		return 1;
	printf("spotread JSON Lines UTF-8 tests passed\n");
	return 0;
}
