RGB_MATRIX_CUSTOM_USER = yes
include keyboards/$(KEYBOARD)/rgb_record/rgb_record.mk
include keyboards/$(KEYBOARD)/wls/wls.mk
include keyboards/$(KEYBOARD)/linker/wireless/wireless.mk

# Windows Dynamic Lighting (USB only)
DYNAMIC_LIGHTING_ENABLE ?= yes
ifeq ($(DYNAMIC_LIGHTING_ENABLE),yes)
OPT_DEFS += -DSPLIT65_LAMPARRAY_ENABLE
EXTRAINCDIRS += keyboards/$(KEYBOARD)/dynamic_lighting
SRC += dynamic_lighting/protocol.c dynamic_lighting/lamparray.c dynamic_lighting/compat.c
endif
