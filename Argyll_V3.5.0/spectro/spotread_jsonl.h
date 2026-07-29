/*
 * IwashiScope machine-readable output for ArgyllCMS spotread.
 * Copyright (C) 2026 Yamonov
 *
 * Licensed under GNU AGPL version 3 only. This file is distributed
 * without any warranty; see ../../LICENSE and ../../NOTICE for details.
 */

#ifndef SPOTREAD_JSONL_H
#define SPOTREAD_JSONL_H

#include "aconfig.h"
#include "numlib.h"
#include "cgats.h"
#include "xspect.h"
#include "insttypes.h"
#include "conv.h"
#include "icoms.h"
#include "inst.h"
#include "tm3015.h"

typedef struct _spotread_jsonl spotread_jsonl;

typedef struct {
	int reading_index;
	const char *mode;
	const xspect *spectrum;
	int has_practical_spectrum_range;
	double practical_spectrum_start_nm;
	double practical_spectrum_end_nm;
	int has_xyz;
	double xyz[3];
	int has_lab;
	double lab[3];
	const char *lab_white_point;
	int has_monochrome;
	double monochrome_y;
	double monochrome_lstar;
	int has_lux;
	double lux;
	int has_cct;
	double cct;
	double duv;
	int has_ev100;
	double ev100;
	int has_planckian;
	double planckian_kelvin;
	double planckian_delta_e;
	int has_daylight;
	double daylight_kelvin;
	double daylight_delta_e;
	int bad_cct;
	int bad_planckian;
	int bad_daylight;
	int has_cri;
	double cri_ra;
	double cri_individual[14];
	int cri_caution;
	int has_tlci;
	double tlci_qa;
	int tlci_caution;
	/* -1 = absent, 0 = valid, 1 = caution, 2 = calculation error. */
	int tm30_status;
	double tm30_rf;
	double tm30_rg;
	double tm30_cct;
	double tm30_duv;
	double tm30_bins[IES_TM_30_15_BINS][2][3];
	double tm30_samples[IES_TM_30_15_ESAMPLES][2][3];
} spotread_jsonl_measurement;

/* Save stdout for JSON Lines and redirect legacy human output to stderr. */
spotread_jsonl *spotread_jsonl_open(void);
void spotread_jsonl_close(spotread_jsonl *p);

void spotread_jsonl_measurement_init(spotread_jsonl_measurement *measurement);

int spotread_jsonl_emit_hello(spotread_jsonl *p, const char *argyll_version);
int spotread_jsonl_emit_instrument(
	spotread_jsonl *p,
	const char *name,
	const char *serial_number
);
int spotread_jsonl_emit_state(spotread_jsonl *p, const char *state);
int spotread_jsonl_emit_calibration(
	spotread_jsonl *p,
	const char *phase,
	inst_cal_cond condition,
	inst_calc_id_type identifier_type,
	const char *identifier,
	inst_code error_code,
	const char *reason
);
int spotread_jsonl_emit_issue(
	spotread_jsonl *p,
	const char *code,
	const char *reason,
	inst_code raw_code,
	const char *recovery
);
int spotread_jsonl_emit_measurement(
	spotread_jsonl *p,
	const spotread_jsonl_measurement *measurement
);

#endif /* SPOTREAD_JSONL_H */
