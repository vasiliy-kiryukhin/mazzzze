// Copyright © 2023 - Kasper Arnklit Frandsen - Royalty Free License
// See the readme.pdf included with the pack

shader_type spatial;
render_mode diffuse_burley;

uniform vec4 albedo_primary : source_color = vec4(1.0);
uniform vec4 albedo_secondary : source_color = vec4(1.0);
uniform float albedo_curvature : hint_range(0.0, 1.0) = 0.15;
uniform float curvature_gamma : hint_range(0.1, 4.0) = 1.0;
uniform float roughness_curvature : hint_range(0.0, 1.0) = 0.15;
uniform float roughness_boost : hint_range(0.0, 1.0) = 0.0;
uniform float specular_amount : hint_range(0.0, 1.0) = 0.5;
uniform float normal_strength_primary : hint_range(0.0, 4.0) = 1.0;
uniform float normal_strength_secondary : hint_range(0.0, 4.0) = 1.0;
uniform float ao_lifght_affect : hint_range(0.0, 1.0) = 0.0;
uniform vec3 detail_scale = vec3(1.0);
uniform vec3 secondary_detail_scale = vec3(1.0);
uniform float secondary_height : hint_range(-2.0, 2.0) = 0.0;
uniform float secondary_height_scale : hint_range(0.01, 1.0) = 1.0;
uniform bool limit_mask_to_direction = true;
uniform vec3 dir_mask_direction = vec3(0.0, 1.0, 0.0);
uniform float dir_mask_offset : hint_range(0.0, 1.0) = 0.5;
uniform float dir_mask_width : hint_range(0.0, 1.0) = 0.1;
uniform bool limit_mask_to_crevices = false;
uniform float crev_mask_offset : hint_range(0.0, 1.0) = 0.5;
uniform float crev_mask_width : hint_range(0.0, 1.0) = 0.1;
uniform bool add_masks = false;

uniform float blend_sharpness = 6.0;
uniform bool fix_tiling = false;
uniform bool preview_mask = false;

uniform sampler2D texture_detail_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_detail_normal : hint_roughness_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_detail_orm : hint_roughness_g, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_detail_height : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_secondary_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_secondary_normal : hint_roughness_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_secondary_orm : hint_roughness_g, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_secondary_height : hint_roughness_g, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_mesh_normal : hint_roughness_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D texture_mesh_oc : hint_default_white, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D texture_fix_tiling_noise : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;

float blend_screen(float c1, float c2, float opacity) {
	return opacity*(1.0-(1.0-c1)*(1.0-c2)) + (1.0-opacity)*c2;
}

vec3 blend_screen_3(vec3 c1, vec3 c2, float opacity) {
	return opacity*(1.0-(1.0-c1)*(1.0-c2)) + (1.0-opacity)*c2;
}

float sum( vec4 v ) { 
	return v.x + v.y + v.z + v.w; 
}

vec3 rand3(vec2 x) {
	return fract(cos(mod(vec3(dot(x, vec2(13.9898, 8.141)),
							  dot(x, vec2(3.4562, 17.398)),
							  dot(x, vec2(13.254, 5.867))), vec3(3.14))) * 43758.5453);
}

// Using iq's technique 3 - https://iquilezles.org/articles/texturerepetition/
vec4 textureNoTile( sampler2D samp, in vec2 uv) {
	// sample variation pattern    
	float k = texture( texture_fix_tiling_noise, 0.005 * uv ).x; // cheap (cache friendly) lookup
    
	// compute index    
	float index = k * 8.0;
	float i = floor( index );
	float f = fract( index );

	float ia = floor(index+0.5);
	float ib = floor(index);
	f = min(f, 1.0-f)*2.0;

	// offsets and scales for the different virtual patterns  
	vec3 offa = (rand3(vec2(3.0,7.0) * ia) - 0.5) * 2.0;
	vec3 offb = (rand3(vec2(3.0,7.0) * ib) - 0.5) * 2.0;

	// compute derivatives for mip-mapping    
	vec2 dx = dFdx(uv), dy = dFdy(uv);
    
	// sample the two closest virtual patterns    
	vec4 cola = textureGrad( samp, uv * (offa.z * + 0.1 + 1.0) + offa.xy, dx, dy );
	vec4 colb = textureGrad( samp, uv + (offb.z * + 0.1 + 1.0) + offb.xy, dx, dy );

	// interpolate between the two virtual patterns    
	return mix(cola, colb, smoothstep(0.2, 0.8, f - 0.1 * sum(cola - colb)));
}

vec4 triplanar_texture(sampler2D p_sampler,vec3 p_weights,vec3 p_triplanar_pos) {
	vec4 samp = vec4(0.0);
	samp += fix_tiling ? textureNoTile(p_sampler,p_triplanar_pos.xy) * p_weights.z : texture(p_sampler,p_triplanar_pos.xy) * p_weights.z;
	samp += fix_tiling ? textureNoTile(p_sampler,p_triplanar_pos.xz) * p_weights.y : texture(p_sampler,p_triplanar_pos.xz) * p_weights.y;
	samp += fix_tiling ? textureNoTile(p_sampler,p_triplanar_pos.zy * vec2(-1.0,1.0)) * p_weights.x : texture(p_sampler,p_triplanar_pos.zy * vec2(-1.0,1.0)) * p_weights.x;
	return samp;
}

// Since Godot store normal maps in 2 channels, this function is used to recalculate normals.
vec3 calculate_normal_z_component(vec3 normal) {
	normal.z = sqrt(1.0 - normal.x * normal.x - normal.y * normal.y);
	return normal;
}

vec3 adjust_normal_map_strength(vec3 normal_map, float strength) {
		normal_map = normal_map * 2.0 - 1.0;
		normal_map = calculate_normal_z_component(normal_map);
		normal_map.xy *= strength;
		return normalize(normal_map) * 0.5 + 0.5;
}

void fragment() {
	// Apply the mesh normal
	vec4 normal_mesh_tex = texture(texture_mesh_normal, UV);
	// It appears that due to the normal map compression there are sometimes errors in the map.
	// Clamping the values slightly fixes the bugs, with almost no visual impact.
	normal_mesh_tex.xy = clamp(normal_mesh_tex.xy, vec2(0.15), vec2(0.85));
	vec3 normal_mesh_tex_unpacked = normal_mesh_tex.rgb * 2.0 - 1.0;
	normal_mesh_tex_unpacked = calculate_normal_z_component(normal_mesh_tex_unpacked);
	// Apply the mesh normal texture in the shader, so we can triplanar textures on top of it.
	NORMAL = normal_mesh_tex_unpacked.x * TANGENT + normal_mesh_tex_unpacked.y * BINORMAL + normal_mesh_tex_unpacked.z * NORMAL;
	
	// World position and normal used for triplanar.
	vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 world_normal = mat3(INV_VIEW_MATRIX) * NORMAL;
	
	// Calculate direction mask
	vec3 dir_normalized = normalize(dir_mask_direction);
	float dir_mask = dot(world_normal, dir_normalized);
	dir_mask = smoothstep(dir_mask_offset - dir_mask_width, dir_mask_offset + dir_mask_width, dir_mask);
	
	// Estimated tangents and binormals for triplanar normal mapping.
	TANGENT = vec3(0.0,0.0,-1.0) * abs(world_normal.x);
	TANGENT+= vec3(1.0,0.0,0.0) * abs(world_normal.y);
	TANGENT+= vec3(1.0,0.0,0.0) * abs(world_normal.z);
	TANGENT = normalize(TANGENT);
	BINORMAL = vec3(0.0,1.0,0.0) * abs(world_normal.x);
	BINORMAL+= vec3(0.0,0.0,-1.0) * abs(world_normal.y);
	BINORMAL+= vec3(0.0,1.0,0.0) * abs(world_normal.z);
	BINORMAL = normalize(BINORMAL);

	vec3 power_normal = pow(abs(world_normal),vec3(blend_sharpness));
	power_normal /= dot(power_normal,vec3(1.0));
	world_pos *= vec3(1.0,-1.0, 1.0);
	
	vec4 occlussion_curvature_mesh_tex = texture(texture_mesh_oc, UV);
	float crev_mask = 1.0 - occlussion_curvature_mesh_tex.r;
	crev_mask = smoothstep(crev_mask_offset - crev_mask_width, crev_mask_offset + crev_mask_width, crev_mask);
	
	vec4 albedo_tex = triplanar_texture(texture_detail_albedo, power_normal, world_pos * detail_scale);
	vec4 orm_tex = triplanar_texture(texture_detail_orm, power_normal, world_pos * detail_scale);
	vec4 sec_orm_tex = triplanar_texture(texture_secondary_orm, power_normal, world_pos * secondary_detail_scale);
	float height_tex = triplanar_texture(texture_detail_height, power_normal, world_pos * detail_scale).r;
	float sec_height_tex = triplanar_texture(texture_secondary_height, power_normal, world_pos * secondary_detail_scale).r;
	sec_height_tex *= secondary_height_scale;
	float mask = 1.0;
	if (add_masks) {
		mask = 0.0;
		mask = limit_mask_to_direction ? 1.0 - ((1.0 - mask) * (1.0 - dir_mask)) : mask;
		mask = limit_mask_to_crevices ? 1.0 - ((1.0 - mask) * (1.0 - crev_mask)) : mask;
	} else {
		mask = limit_mask_to_direction ? mask * dir_mask : mask;
		mask = limit_mask_to_crevices ? mask * crev_mask : mask;
	}
	mask = clamp((height_tex - (sec_height_tex * mask + secondary_height * mask)) / -0.2, 0.0, 1.0);
	vec4 sec_albedo_tex = triplanar_texture(texture_secondary_albedo, power_normal, world_pos * secondary_detail_scale);
	float curv = pow(occlussion_curvature_mesh_tex.g, curvature_gamma);
	vec3 base_albedo = blend_screen_3(vec3(curv), albedo_primary.rgb * albedo_tex.rgb, albedo_curvature);
	ALBEDO = mix(base_albedo, sec_albedo_tex.rgb * albedo_secondary.rgb, mask);
	
	float base_roughness = blend_screen(occlussion_curvature_mesh_tex.g, orm_tex.g, roughness_curvature);
	ROUGHNESS = mix(base_roughness, sec_orm_tex.g, mask);
	ROUGHNESS = mix(ROUGHNESS, 1.0, roughness_boost);
	SPECULAR = specular_amount;
	vec3 base_normal_map = triplanar_texture(texture_detail_normal, power_normal, world_pos * detail_scale).rgb;
	base_normal_map = adjust_normal_map_strength(base_normal_map, normal_strength_primary);
	vec3 secondary_normal_map = triplanar_texture(texture_secondary_normal, power_normal, world_pos * secondary_detail_scale).rgb;
	secondary_normal_map = adjust_normal_map_strength(secondary_normal_map, normal_strength_secondary);
	NORMAL_MAP = mix(base_normal_map, secondary_normal_map, mask);
	float base_ao = orm_tex.r * occlussion_curvature_mesh_tex.r;
	AO = mix(base_ao, sec_orm_tex.r, mask);
	AO_LIGHT_AFFECT = ao_lifght_affect;
	
	if (preview_mask) {
		ALBEDO = vec3(mask);
		ROUGHNESS = 1.0;
		NORMAL_MAP = vec3(0.5);
		AO = 0.0;
	}
}
