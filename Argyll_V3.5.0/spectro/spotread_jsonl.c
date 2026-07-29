/*
 * IwashiScope machine-readable output for ArgyllCMS spotread.
 * Copyright (C) 2026 Yamonov
 *
 * This file is free software: you can redistribute it and/or modify it
 * under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, version 3 of the License.
 *
 * This file is distributed without any warranty; see ../../LICENSE and
 * ../../NOTICE for details.
 */

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(NT)
# include <io.h>
# define SPJ_DUP _dup
# define SPJ_DUP2 _dup2
# define SPJ_FILENO _fileno
# define SPJ_FDOPEN _fdopen
#else
# include <unistd.h>
# define SPJ_DUP dup
# define SPJ_DUP2 dup2
# define SPJ_FILENO fileno
# define SPJ_FDOPEN fdopen
#endif

#include "spotread_jsonl.h"

/* ArgyllCMS and YAJL use different private meanings for this macro. */
#ifdef CF64PREC
# undef CF64PREC
#endif
#include "yajl_gen.h"

#define SPOTREAD_JSONL_PROTOCOL_VERSION 3
#define IWASHISCOPE_IMPLEMENTATION "IwashiScope spot reader"
#define IWASHISCOPE_IMPLEMENTATION_VERSION 1

struct _spotread_jsonl {
	FILE *stream;
};

typedef struct {
	yajl_gen generator;
	int failed;
} json_writer;

static void record_status(json_writer *writer, yajl_gen_status status) {
	if (status != yajl_gen_status_ok)
		writer->failed = 1;
}

static size_t valid_utf8_sequence_length(
	const unsigned char *value,
	size_t remaining
) {
	unsigned char first;
	unsigned char second;
	unsigned char third;
	unsigned char fourth;

	if (remaining == 0)
		return 0;
	first = value[0];
	if (first <= 0x7f)
		return 1;
	if (remaining < 2)
		return 0;
	second = value[1];

	if (first >= 0xc2 && first <= 0xdf)
		return second >= 0x80 && second <= 0xbf ? 2 : 0;

	if (remaining < 3)
		return 0;
	third = value[2];
	if (third < 0x80 || third > 0xbf)
		return 0;
	if (first == 0xe0)
		return second >= 0xa0 && second <= 0xbf ? 3 : 0;
	if (first >= 0xe1 && first <= 0xec)
		return second >= 0x80 && second <= 0xbf ? 3 : 0;
	if (first == 0xed)
		return second >= 0x80 && second <= 0x9f ? 3 : 0;
	if (first >= 0xee && first <= 0xef)
		return second >= 0x80 && second <= 0xbf ? 3 : 0;

	if (remaining < 4)
		return 0;
	fourth = value[3];
	if (fourth < 0x80 || fourth > 0xbf)
		return 0;
	if (first == 0xf0)
		return second >= 0x90 && second <= 0xbf ? 4 : 0;
	if (first >= 0xf1 && first <= 0xf3)
		return second >= 0x80 && second <= 0xbf ? 4 : 0;
	if (first == 0xf4)
		return second >= 0x80 && second <= 0x8f ? 4 : 0;
	return 0;
}

static void json_string(json_writer *writer, const char *value) {
	const unsigned char *input;
	unsigned char *sanitized;
	size_t input_length;
	size_t input_offset;
	size_t output_length;
	size_t sequence_length;
	int needs_sanitization = 0;

	if (writer->failed)
		return;
	if (value == NULL) {
		record_status(writer, yajl_gen_null(writer->generator));
		return;
	}
	input = (const unsigned char *)value;
	input_length = strlen(value);
	for (input_offset = 0; input_offset < input_length;) {
		sequence_length = valid_utf8_sequence_length(
			input + input_offset,
			input_length - input_offset
		);
		if (sequence_length == 0) {
			needs_sanitization = 1;
			break;
		}
		input_offset += sequence_length;
	}
	if (!needs_sanitization) {
		record_status(writer, yajl_gen_string(
			writer->generator,
			input,
			input_length
		));
		return;
	}
	if (input_length > (((size_t)-1) - 1) / 3) {
		writer->failed = 1;
		return;
	}
	sanitized = (unsigned char *)malloc(input_length * 3 + 1);
	if (sanitized == NULL) {
		writer->failed = 1;
		return;
	}
	input_offset = 0;
	output_length = 0;
	while (input_offset < input_length) {
		sequence_length = valid_utf8_sequence_length(
			input + input_offset,
			input_length - input_offset
		);
		if (sequence_length == 0) {
			sanitized[output_length++] = 0xef;
			sanitized[output_length++] = 0xbf;
			sanitized[output_length++] = 0xbd;
			input_offset++;
			continue;
		}
		memcpy(
			sanitized + output_length,
			input + input_offset,
			sequence_length
		);
		output_length += sequence_length;
		input_offset += sequence_length;
	}
	sanitized[output_length] = '\0';
	record_status(writer, yajl_gen_string(
		writer->generator,
		sanitized,
		output_length
	));
	free(sanitized);
}

static void json_key(json_writer *writer, const char *key) {
	json_string(writer, key);
}

static void json_integer(json_writer *writer, longlong value) {
	if (!writer->failed)
		record_status(writer, yajl_gen_integer(writer->generator, value));
}

static void json_double(json_writer *writer, double value) {
	if (writer->failed)
		return;
	if (!isfinite(value)) {
		writer->failed = 1;
		return;
	}
	record_status(writer, yajl_gen_double(writer->generator, value));
}

static void json_bool(json_writer *writer, int value) {
	if (!writer->failed)
		record_status(writer, yajl_gen_bool(writer->generator, value != 0));
}

static void json_map_open(json_writer *writer) {
	if (!writer->failed)
		record_status(writer, yajl_gen_map_open(writer->generator));
}

static void json_map_close(json_writer *writer) {
	if (!writer->failed)
		record_status(writer, yajl_gen_map_close(writer->generator));
}

static void json_array_open(json_writer *writer) {
	if (!writer->failed)
		record_status(writer, yajl_gen_array_open(writer->generator));
}

static void json_array_close(json_writer *writer) {
	if (!writer->failed)
		record_status(writer, yajl_gen_array_close(writer->generator));
}

static void json_vector3(json_writer *writer, const double value[3]) {
	int i;
	json_array_open(writer);
	for (i = 0; i < 3; i++)
		json_double(writer, value[i]);
	json_array_close(writer);
}

static json_writer begin_event(const char *event) {
	json_writer writer;
	writer.generator = yajl_gen_alloc(NULL);
	writer.failed = writer.generator == NULL;
	if (!writer.failed) {
		if (!yajl_gen_config(writer.generator, yajl_gen_validate_utf8, 1))
			writer.failed = 1;
	}
	json_map_open(&writer);
	json_key(&writer, "protocolVersion");
	json_integer(&writer, SPOTREAD_JSONL_PROTOCOL_VERSION);
	json_key(&writer, "event");
	json_string(&writer, event);
	return writer;
}

static int finish_event(spotread_jsonl *p, json_writer *writer) {
	const unsigned char *buffer = NULL;
	size_t length = 0;
	int failed;

	json_map_close(writer);
	if (!writer->failed
	 && yajl_gen_get_buf(writer->generator, &buffer, &length) != yajl_gen_status_ok)
		writer->failed = 1;

	if (!writer->failed) {
		if (fwrite(buffer, 1, length, p->stream) != length
		 || fputc('\n', p->stream) == EOF
		 || fflush(p->stream) != 0)
			writer->failed = 1;
	}

	failed = writer->failed;
	if (writer->generator != NULL)
		yajl_gen_free(writer->generator);
	return failed ? -1 : 0;
}

static const char *condition_name(inst_cal_cond condition) {
	switch (condition & inst_calc_cond_mask) {
		case inst_calc_none: return "none";
		case inst_calc_uop_ref_white: return "userOperatedReflectiveWhite";
		case inst_calc_uop_trans_white: return "userOperatedTransmissiveWhite";
		case inst_calc_uop_trans_dark: return "userOperatedTransmissiveDark";
		case inst_calc_man_ref_white: return "reflectiveWhite";
		case inst_calc_man_ref_whitek: return "reflectiveWhiteClick";
		case inst_calc_man_ref_dark: return "reflectiveDark";
		case inst_calc_man_dark_gloss: return "glossBlack";
		case inst_calc_man_em_dark: return "emissiveDark";
		case inst_calc_man_am_dark: return "ambientDark";
		case inst_calc_man_cal_smode: return "sensorCalibrationPosition";
		case inst_calc_man_trans_white: return "transmissiveWhite";
		case inst_calc_man_trans_dark: return "transmissiveDark";
		case inst_calc_emis_white: return "emissiveWhite";
		case inst_calc_emis_80pc: return "emissive80Percent";
		case inst_calc_emis_grey: return "emissiveGrey";
		case inst_calc_emis_grey_darker: return "emissiveGreyDarker";
		case inst_calc_emis_grey_ligher: return "emissiveGreyLighter";
		case inst_calc_change_filter: return "changeFilter";
		case inst_calc_message: return "message";
		default: return "unknown";
	}
}

static const char *identifier_type_name(inst_calc_id_type type) {
	switch (type) {
		case inst_calc_id_none: return "none";
		case inst_calc_id_ref_sn: return "referenceSerialNumber";
		case inst_calc_id_trans_low: return "transmissionLow";
		case inst_calc_id_trans_wl: return "transmissionLowAtWavelengths";
		case inst_calc_id_filt_unkn: return "filterUnknown";
		case inst_calc_id_filt_none: return "filterM0";
		case inst_calc_id_filt_D50: return "filterD50";
		case inst_calc_id_filt_D65: return "filterD65";
		case inst_calc_id_filt_UV: return "filterM2";
		case inst_calc_id_filt_pol: return "filterM3";
		case inst_calc_id_filt_cust: return "filterCustom";
		default: return "unknown";
	}
}

static int tm30_bins_are_finite(
	const double bins[IES_TM_30_15_BINS][2][3]
) {
	int i, j, k;
	for (i = 0; i < IES_TM_30_15_BINS; i++)
		for (j = 0; j < 2; j++)
			for (k = 0; k < 3; k++)
				if (!isfinite(bins[i][j][k]))
					return 0;
	return 1;
}

static int tm30_samples_are_finite(
	const double samples[IES_TM_30_15_ESAMPLES][2][3]
) {
	int i, j, k;
	for (i = 0; i < IES_TM_30_15_ESAMPLES; i++)
		for (j = 0; j < 2; j++)
			for (k = 0; k < 3; k++)
				if (!isfinite(samples[i][j][k]))
					return 0;
	return 1;
}

spotread_jsonl *spotread_jsonl_open(void) {
	spotread_jsonl *p;
	int protocol_fd;
	FILE *protocol_stream;

	fflush(stdout);
	protocol_fd = SPJ_DUP(SPJ_FILENO(stdout));
	if (protocol_fd < 0)
		return NULL;
	if (SPJ_DUP2(SPJ_FILENO(stderr), SPJ_FILENO(stdout)) < 0) {
#if defined(NT)
		_close(protocol_fd);
#else
		close(protocol_fd);
#endif
		return NULL;
	}
	protocol_stream = SPJ_FDOPEN(protocol_fd, "w");
	if (protocol_stream == NULL) {
#if defined(NT)
		_close(protocol_fd);
#else
		close(protocol_fd);
#endif
		return NULL;
	}
	setvbuf(stdout, NULL, _IONBF, 0);
	setvbuf(protocol_stream, NULL, _IONBF, 0);

	p = (spotread_jsonl *)calloc(1, sizeof(*p));
	if (p == NULL) {
		fclose(protocol_stream);
		return NULL;
	}
	p->stream = protocol_stream;
	return p;
}

void spotread_jsonl_close(spotread_jsonl *p) {
	if (p == NULL)
		return;
	if (p->stream != NULL)
		fclose(p->stream);
	free(p);
}

void spotread_jsonl_measurement_init(spotread_jsonl_measurement *measurement) {
	memset(measurement, 0, sizeof(*measurement));
	measurement->tm30_status = -1;
}

int spotread_jsonl_emit_hello(spotread_jsonl *p, const char *argyll_version) {
	json_writer writer;
	if (p == NULL)
		return 0;
	writer = begin_event("hello");
	json_key(&writer, "implementation");
	json_string(&writer, IWASHISCOPE_IMPLEMENTATION);
	json_key(&writer, "implementationVersion");
	json_integer(&writer, IWASHISCOPE_IMPLEMENTATION_VERSION);
	json_key(&writer, "argyllVersion");
	json_string(&writer, argyll_version);
	return finish_event(p, &writer);
}

int spotread_jsonl_emit_instrument(
	spotread_jsonl *p,
	const char *name,
	const char *serial_number
) {
	json_writer writer;
	if (p == NULL)
		return 0;
	writer = begin_event("instrument");
	json_key(&writer, "name");
	json_string(&writer, name);
	json_key(&writer, "serialNumber");
	json_string(&writer, serial_number != NULL && serial_number[0] != '\0'
		? serial_number : NULL);
	return finish_event(p, &writer);
}

int spotread_jsonl_emit_state(spotread_jsonl *p, const char *state) {
	json_writer writer;
	if (p == NULL)
		return 0;
	writer = begin_event("state");
	json_key(&writer, "state");
	json_string(&writer, state);
	return finish_event(p, &writer);
}

int spotread_jsonl_emit_calibration(
	spotread_jsonl *p,
	const char *phase,
	inst_cal_cond condition,
	inst_calc_id_type identifier_type,
	const char *identifier,
	inst_code error_code,
	const char *reason
) {
	json_writer writer;
	if (p == NULL)
		return 0;
	writer = begin_event("calibration");
	json_key(&writer, "phase");
	json_string(&writer, phase);
	json_key(&writer, "condition");
	json_string(&writer, condition_name(condition));
	json_key(&writer, "identifierType");
	json_string(&writer, identifier_type_name(identifier_type));
	json_key(&writer, "identifier");
	json_string(&writer, identifier != NULL && identifier[0] != '\0' ? identifier : NULL);
	json_key(&writer, "optional");
	json_bool(&writer, (condition & inst_calc_optional_flag) != 0);
	json_key(&writer, "requiresConfirmation");
	json_bool(&writer,
		(condition & inst_calc_cond_mask) != inst_calc_none
		&& (condition & inst_calc_cond_mask) != inst_calc_man_ref_whitek);
	json_key(&writer, "errorCode");
	json_integer(&writer, error_code);
	json_key(&writer, "reason");
	json_string(&writer, reason);
	return finish_event(p, &writer);
}

int spotread_jsonl_emit_issue(
	spotread_jsonl *p,
	const char *code,
	const char *reason,
	inst_code raw_code,
	const char *recovery
) {
	json_writer writer;
	if (p == NULL)
		return 0;
	writer = begin_event("issue");
	json_key(&writer, "code");
	json_string(&writer, code);
	json_key(&writer, "reason");
	json_string(&writer, reason);
	json_key(&writer, "rawCode");
	json_integer(&writer, raw_code);
	json_key(&writer, "recovery");
	json_string(&writer, recovery);
	return finish_event(p, &writer);
}

int spotread_jsonl_emit_measurement(
	spotread_jsonl *p,
	const spotread_jsonl_measurement *measurement
) {
	json_writer writer;
	int i;
	if (p == NULL)
		return 0;
	writer = begin_event("measurement");
	json_key(&writer, "mode");
	json_string(&writer, measurement->mode);
	json_key(&writer, "readingIndex");
	json_integer(&writer, measurement->reading_index);

	if (measurement->spectrum != NULL && measurement->spectrum->spec_n > 0) {
		json_key(&writer, "spectrum");
		json_map_open(&writer);
		json_key(&writer, "startNm");
		json_double(&writer, measurement->spectrum->spec_wl_short);
		json_key(&writer, "endNm");
		json_double(&writer, measurement->spectrum->spec_wl_long);
		if (measurement->has_practical_spectrum_range) {
			json_key(&writer, "practicalStartNm");
			json_double(&writer, measurement->practical_spectrum_start_nm);
			json_key(&writer, "practicalEndNm");
			json_double(&writer, measurement->practical_spectrum_end_nm);
		}
		json_key(&writer, "values");
		json_array_open(&writer);
		for (i = 0; i < measurement->spectrum->spec_n; i++)
			json_double(&writer, measurement->spectrum->spec[i]);
		json_array_close(&writer);
		json_map_close(&writer);
	}

	if (measurement->has_xyz) {
		json_key(&writer, "xyz");
		json_vector3(&writer, measurement->xyz);
	}
	if (measurement->has_lab) {
		json_key(&writer, "lab");
		json_vector3(&writer, measurement->lab);
		json_key(&writer, "labWhitePoint");
		json_string(&writer, measurement->lab_white_point);
	}
	if (measurement->has_monochrome) {
		json_key(&writer, "monochrome");
		json_map_open(&writer);
		json_key(&writer, "y");
		json_double(&writer, measurement->monochrome_y);
		json_key(&writer, "lStar");
		json_double(&writer, measurement->monochrome_lstar);
		json_map_close(&writer);
	}
	if (measurement->has_lux) {
		json_key(&writer, "lux");
		json_double(&writer, measurement->lux);
	}
	if (measurement->has_cct) {
		json_key(&writer, "cct");
		json_double(&writer, measurement->cct);
		json_key(&writer, "duv");
		json_double(&writer, measurement->duv);
	}
	if (measurement->has_ev100) {
		json_key(&writer, "suggestedEV100");
		json_double(&writer, measurement->ev100);
	}
	if (measurement->has_planckian) {
		json_key(&writer, "closestPlanckian");
		json_map_open(&writer);
		json_key(&writer, "kelvin");
		json_double(&writer, measurement->planckian_kelvin);
		json_key(&writer, "deltaE2000");
		json_double(&writer, measurement->planckian_delta_e);
		json_map_close(&writer);
	}
	if (measurement->has_daylight) {
		json_key(&writer, "closestDaylight");
		json_map_open(&writer);
		json_key(&writer, "kelvin");
		json_double(&writer, measurement->daylight_kelvin);
		json_key(&writer, "deltaE2000");
		json_double(&writer, measurement->daylight_delta_e);
		json_map_close(&writer);
	}

	json_key(&writer, "lightingMetricIssues");
	json_array_open(&writer);
	if (measurement->bad_cct)
		json_string(&writer, "invalidCCT");
	if (measurement->bad_planckian)
		json_string(&writer, "invalidPlanckianTemperature");
	if (measurement->bad_daylight)
		json_string(&writer, "invalidDaylightTemperature");
	json_array_close(&writer);

	if (measurement->has_cri) {
		json_key(&writer, "cri");
		json_map_open(&writer);
		json_key(&writer, "ra");
		json_double(&writer, measurement->cri_ra);
		json_key(&writer, "individual");
		json_array_open(&writer);
		for (i = 0; i < 14; i++)
			json_double(&writer, measurement->cri_individual[i]);
		json_array_close(&writer);
		json_key(&writer, "caution");
		json_bool(&writer, measurement->cri_caution);
		json_map_close(&writer);
	}
	if (measurement->has_tlci) {
		json_key(&writer, "tlci");
		json_map_open(&writer);
		json_key(&writer, "qa");
		json_double(&writer, measurement->tlci_qa);
		json_key(&writer, "caution");
		json_bool(&writer, measurement->tlci_caution);
		json_map_close(&writer);
	}
	if (measurement->tm30_status >= 0) {
		int bins_valid = measurement->tm30_status < 2
			&& tm30_bins_are_finite(measurement->tm30_bins);
		int samples_valid = measurement->tm30_status < 2
			&& tm30_samples_are_finite(measurement->tm30_samples);
		json_key(&writer, "tm30");
		json_map_open(&writer);
		json_key(&writer, "status");
		json_string(&writer, measurement->tm30_status == 0 && bins_valid && samples_valid
			? "valid" : measurement->tm30_status == 1 && bins_valid && samples_valid
			? "caution" : "error");
		if (measurement->tm30_status < 2 && bins_valid && samples_valid) {
			json_key(&writer, "rf");
			json_double(&writer, measurement->tm30_rf);
			json_key(&writer, "rg");
			json_double(&writer, measurement->tm30_rg);
			json_key(&writer, "cct");
			json_double(&writer, measurement->tm30_cct);
			json_key(&writer, "duv");
			json_double(&writer, measurement->tm30_duv);
			json_key(&writer, "bins");
			json_array_open(&writer);
			for (i = 0; i < IES_TM_30_15_BINS; i++) {
				json_map_open(&writer);
				json_key(&writer, "index");
				json_integer(&writer, i + 1);
				json_key(&writer, "referenceJab");
				json_vector3(&writer, measurement->tm30_bins[i][0]);
				json_key(&writer, "testJab");
				json_vector3(&writer, measurement->tm30_bins[i][1]);
				json_map_close(&writer);
			}
			json_array_close(&writer);
			json_key(&writer, "samples");
			json_array_open(&writer);
			for (i = 0; i < IES_TM_30_15_ESAMPLES; i++) {
				json_map_open(&writer);
				json_key(&writer, "index");
				json_integer(&writer, i + 1);
				json_key(&writer, "referenceJab");
				json_vector3(&writer, measurement->tm30_samples[i][0]);
				json_key(&writer, "testJab");
				json_vector3(&writer, measurement->tm30_samples[i][1]);
				json_map_close(&writer);
			}
			json_array_close(&writer);
		}
		json_map_close(&writer);
	}

	return finish_event(p, &writer);
}
