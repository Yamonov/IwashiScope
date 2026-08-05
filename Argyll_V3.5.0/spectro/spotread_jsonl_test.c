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
#include "xicc.h"
#include "ui.h"

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

static const char valid_analysis_request_json[] =
	"{\"protocolVersion\":3,\"command\":\"analyzeSpectrum\","
	"\"requestId\":\"average-10\",\"mode\":\"ambient\","
	"\"sampleCount\":10,\"spectrum\":{\"startNm\":380,"
	"\"endNm\":730,\"norm\":1,\"practicalStartNm\":400,"
	"\"practicalEndNm\":730,\"values\":[1.0,2.5,3.0]}}";

static int expect_analysis_request_parser(void) {
	spotread_jsonl_analysis_request request;
	spotread_jsonl_analysis_status status;
	char error_message[256];
	int failed = 0;

	status = spotread_jsonl_parse_analysis_request(
		valid_analysis_request_json,
		strlen(valid_analysis_request_json),
		"ambient",
		&request,
		error_message,
		sizeof(error_message)
	);
	if (status != spotread_jsonl_analysis_ok) {
		fprintf(stderr, "valid spectrum analysis request failed: %s\n",
			error_message);
		failed = 1;
	} else if (strcmp(request.request_id, "average-10") != 0
	        || strcmp(request.mode, "ambient") != 0
	        || request.sample_count != 10
	        || request.spectrum.spec_n != 3
	        || request.spectrum.spec_wl_short != 380.0
	        || request.spectrum.spec_wl_long != 730.0
	        || request.spectrum.norm != 1.0
	        || request.spectrum.spec[1] != 2.5
	        || !request.has_practical_spectrum_range
	        || request.practical_spectrum_start_nm != 400.0
	        || request.practical_spectrum_end_nm != 730.0) {
		fprintf(stderr, "valid spectrum analysis request decoded incorrectly\n");
		failed = 1;
	}

	status = spotread_jsonl_parse_analysis_request(
		valid_analysis_request_json,
		strlen(valid_analysis_request_json),
		"reflectance",
		&request,
		error_message,
		sizeof(error_message)
	);
	if (status != spotread_jsonl_analysis_invalid
	 || strstr(error_message, "mode") == NULL) {
		fprintf(stderr, "measurement-mode mismatch was not rejected\n");
		failed = 1;
	}

	{
		static const char invalid_norm_json[] =
			"{\"protocolVersion\":3,\"command\":\"analyzeSpectrum\","
			"\"requestId\":\"average-6\",\"mode\":\"reflectance\","
			"\"sampleCount\":6,\"spectrum\":{\"startNm\":380,"
			"\"endNm\":730,\"norm\":1,\"values\":[1,2,3]}}";
		status = spotread_jsonl_parse_analysis_request(
			invalid_norm_json,
			strlen(invalid_norm_json),
			"reflectance",
			&request,
			error_message,
			sizeof(error_message)
		);
		if (status != spotread_jsonl_analysis_invalid
		 || strstr(error_message, "norm") == NULL) {
			fprintf(stderr, "mode-specific spectrum norm was not enforced\n");
			failed = 1;
		}
	}

	{
		static const char incomplete_range_json[] =
			"{\"protocolVersion\":3,\"command\":\"analyzeSpectrum\","
			"\"requestId\":\"average-6\",\"mode\":\"ambient\","
			"\"sampleCount\":6,\"spectrum\":{\"startNm\":380,"
			"\"endNm\":730,\"norm\":1,\"practicalStartNm\":400,"
			"\"values\":[1,2,3]}}";
		status = spotread_jsonl_parse_analysis_request(
			incomplete_range_json,
			strlen(incomplete_range_json),
			"ambient",
			&request,
			error_message,
			sizeof(error_message)
		);
		if (status != spotread_jsonl_analysis_invalid
		 || strstr(error_message, "practical wavelength range") == NULL) {
			fprintf(stderr, "incomplete practical range was not rejected\n");
			failed = 1;
		}
	}

	{
		static const char nested_protocol_version_json[] =
			"{\"metadata\":{\"protocolVersion\":3},"
			"\"command\":\"analyzeSpectrum\","
			"\"requestId\":\"average-6\",\"mode\":\"ambient\","
			"\"sampleCount\":6,\"spectrum\":{\"startNm\":380,"
			"\"endNm\":730,\"norm\":1,\"values\":[1,2,3]}}";
		status = spotread_jsonl_parse_analysis_request(
			nested_protocol_version_json,
			strlen(nested_protocol_version_json),
			"ambient",
			&request,
			error_message,
			sizeof(error_message)
		);
		if (status != spotread_jsonl_analysis_invalid
		 || strstr(error_message, "protocol version") == NULL) {
			fprintf(stderr, "nested protocol field was accepted as top-level\n");
			failed = 1;
		}
	}

	return failed;
}

#if !defined(NT)
static int expect_framed_analysis_request(void) {
	unsigned char header[4];
	size_t payload_length = strlen(valid_analysis_request_json);
	int input_pipe[2];
	int saved_stdin;
	spotread_jsonl_analysis_request request;
	spotread_jsonl_analysis_status status;
	char error_message[256];
	int failed = 0;

	header[0] = (unsigned char)((payload_length >> 24) & 0xff);
	header[1] = (unsigned char)((payload_length >> 16) & 0xff);
	header[2] = (unsigned char)((payload_length >> 8) & 0xff);
	header[3] = (unsigned char)(payload_length & 0xff);
	if (pipe(input_pipe) != 0) {
		fprintf(stderr, "unable to create framed-input test pipe\n");
		return 1;
	}
	if (write(input_pipe[1], header, sizeof(header)) != sizeof(header)
	 || write(input_pipe[1], valid_analysis_request_json, payload_length)
		!= (ssize_t)payload_length) {
		fprintf(stderr, "unable to write framed-input test data\n");
		close(input_pipe[0]);
		close(input_pipe[1]);
		return 1;
	}
	close(input_pipe[1]);
	saved_stdin = dup(STDIN_FILENO);
	if (saved_stdin < 0 || dup2(input_pipe[0], STDIN_FILENO) < 0) {
		fprintf(stderr, "unable to redirect framed-input test stdin\n");
		close(input_pipe[0]);
		if (saved_stdin >= 0)
			close(saved_stdin);
		return 1;
	}
	close(input_pipe[0]);
	status = spotread_jsonl_read_analysis_request(
		"ambient",
		&request,
		error_message,
		sizeof(error_message)
	);
	if (dup2(saved_stdin, STDIN_FILENO) < 0) {
		fprintf(stderr, "unable to restore stdin after framed-input test\n");
		failed = 1;
	}
	close(saved_stdin);
	if (status != spotread_jsonl_analysis_ok
	 || strcmp(request.request_id, "average-10") != 0) {
		fprintf(stderr, "framed spectrum analysis request failed: %s\n",
			error_message);
		failed = 1;
	}
	return failed;
}
#endif

static int expect_analysis_protocol_metadata(void) {
	spotread_jsonl protocol;
	spotread_jsonl_measurement measurement;
	xspect spectrum;
	char output[4096];
	size_t output_length;
	int failed = 0;

	protocol.stream = tmpfile();
	if (protocol.stream == NULL) {
		fprintf(stderr, "unable to create protocol metadata test stream\n");
		return 1;
	}
	if (spotread_jsonl_emit_hello(&protocol, "3.5.0") != 0) {
		fprintf(stderr, "unable to emit protocol hello\n");
		failed = 1;
	}
	spotread_jsonl_measurement_init(&measurement);
	memset(&spectrum, 0, sizeof(spectrum));
	XSPECT_SET_INFO(&spectrum, 3, 380.0, 730.0, 1.0);
	spectrum.spec[0] = 1.0;
	spectrum.spec[1] = 2.0;
	spectrum.spec[2] = 3.0;
	measurement.reading_index = 11;
	measurement.mode = "ambient";
	measurement.source = "averagedSpectrum";
	measurement.analysis_request_id = "average-10";
	measurement.averaged_sample_count = 10;
	measurement.spectrum = &spectrum;
	measurement.has_xyz = 1;
	measurement.xyz[0] = 1.0;
	measurement.xyz[1] = 2.0;
	measurement.xyz[2] = 3.0;
	measurement.has_lab = 1;
	measurement.lab[0] = 4.0;
	measurement.lab[1] = 5.0;
	measurement.lab[2] = 6.0;
	measurement.lab_white_point = "D50";
	if (spotread_jsonl_emit_measurement(&protocol, &measurement) != 0) {
		fprintf(stderr, "unable to emit analysis measurement metadata\n");
		failed = 1;
	}
	rewind(protocol.stream);
	output_length = fread(output, 1, sizeof(output) - 1, protocol.stream);
	output[output_length] = '\0';
	if (strstr(output, "\"capabilities\":[\"spectrumAnalysisV1\"]") == NULL
	 || strstr(output, "\"source\":\"averagedSpectrum\"") == NULL
	 || strstr(output, "\"analysisRequestId\":\"average-10\"") == NULL
	 || strstr(output, "\"averagedSampleCount\":10") == NULL) {
		fprintf(stderr, "analysis protocol metadata is incomplete\n");
		failed = 1;
	}
	fclose(protocol.stream);
	return failed;
}

static int expect_virtual_spectrum_calculations(void) {
	xspect spectrum;
	xspect custom_illuminant;
	xspect custom_observer[3];
	xsp2cie *converter;
	double xyz[3];
	double lab[3];
	double cri_individual[14];
	double cri;
	double tlci;
	double tm30_rf;
	double tm30_rg;
	double tm30_cct;
	double tm30_duv;
	double tm30_bins[IES_TM_30_15_BINS][2][3];
	double tm30_samples[IES_TM_30_15_ESAMPLES][2][3];
	int cri_invalid = 0;
	int tlci_invalid = 0;
	int tm30_status;
	int i;
	int failed = 0;

	memset(&spectrum, 0, sizeof(spectrum));
	memset(&custom_illuminant, 0, sizeof(custom_illuminant));
	memset(custom_observer, 0, sizeof(custom_observer));
	XSPECT_SET_INFO(&spectrum, 36, 380.0, 730.0, 1.0);
	for (i = 0; i < spectrum.spec_n; i++)
		spectrum.spec[i] = 1.0;
	converter = new_xsp2cie(
		icxIT_none,
		0.0,
		&custom_illuminant,
		icxOT_default,
		custom_observer,
		icSigXYZData,
		icxClamp
	);
	if (converter == NULL) {
		fprintf(stderr, "unable to create virtual-spectrum XYZ converter\n");
		return 1;
	}
	converter->convert(converter, xyz, &spectrum);
	icmXYZ2Lab(&icmD50_100, lab, xyz);
	cri = icx_CIE1995_CRI(&cri_invalid, cri_individual, &spectrum);
	tlci = icx_EBU2012_TLCI(&tlci_invalid, &spectrum);
	tm30_status = icx_IES_TM_30_15(
		&tm30_rf,
		&tm30_rg,
		&tm30_cct,
		&tm30_duv,
		tm30_bins,
		tm30_samples,
		&spectrum
	);
	converter->del(converter);

	if (!isfinite(xyz[0]) || !isfinite(xyz[1]) || !isfinite(xyz[2])
	 || xyz[1] <= 0.0
	 || !isfinite(lab[0]) || !isfinite(lab[1]) || !isfinite(lab[2])
	 || !isfinite(cri) || !isfinite(tlci)
	 || tm30_status >= 2
	 || !isfinite(tm30_rf) || !isfinite(tm30_rg)
	 || !isfinite(tm30_cct) || !isfinite(tm30_duv)) {
		fprintf(stderr, "virtual spectrum did not produce complete metrics\n");
		failed = 1;
	}
	return failed;
}

int main(int argc, char *argv[]) {
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
	(void)argc;
	(void)argv;

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
	failed |= expect_analysis_request_parser();
#if !defined(NT)
	failed |= expect_framed_analysis_request();
#endif
	failed |= expect_analysis_protocol_metadata();
	failed |= expect_virtual_spectrum_calculations();

	if (failed)
		return 1;
	printf("spotread JSON Lines and spectrum analysis tests passed\n");
	return 0;
}
