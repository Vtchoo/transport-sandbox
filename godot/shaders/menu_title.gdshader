shader_type canvas_item;
uniform sampler2D screen_texture:hint_screen_texture,repeat_disable,filter_linear_mipmap;
void fragment(){
	vec4 original=textureLod(screen_texture,SCREEN_UV,0.0);
	float soft=0.0;float close_shadow=0.0;float weights=0.0;
	for(int x=-5;x<=5;x++)for(int y=-5;y<=5;y++){
		vec2 offset=vec2(float(x),float(y));
		float weight=exp(-dot(offset,offset)/8.0);
		soft+=textureLod(screen_texture,SCREEN_UV+(offset*6.25-vec2(0.0,3.0))*SCREEN_PIXEL_SIZE,0.0).a*weight;
		close_shadow+=textureLod(screen_texture,SCREEN_UV+(offset*1.25-vec2(0.0,1.0))*SCREEN_PIXEL_SIZE,0.0).a*weight;
		weights+=weight;
	}
	float shadow_alpha=1.0-(1.0-soft/weights*0.788235)*(1.0-close_shadow/weights);
	float alpha=original.a+shadow_alpha*(1.0-original.a);
	vec3 rgb=(original.rgb+vec3(16.0,32.0,25.0)/255.0*shadow_alpha*(1.0-original.a))/max(alpha,0.00001);
	COLOR=vec4(rgb,alpha);
}
